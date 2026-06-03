using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// RED regression for the StartAsync/StopAsync concurrency defect in <see cref="RpcHost"/>.
///
/// When <c>StopAsync</c> runs while <c>StartAsync</c> is still blocked inside
/// <c>_listener.StartAsync</c>, <c>StopAsync</c> installs a running <c>StopCoreAsync</c>
/// (which has already reached <c>_listener.StopAsync</c>). The cancellation then makes
/// <c>_listener.StartAsync</c> throw, and <c>StartAsync</c>'s catch block clears
/// <c>_cts</c>/<c>_stopTask</c> because <c>ReferenceEquals(_cts, cts)</c> is true — hiding the
/// still-running <c>StopCoreAsync</c> from any future Stop/Dispose. A subsequent
/// <c>DisposeAsync</c> then sees <c>_cts == null</c>, returns immediately from <c>StopAsync</c>,
/// and proceeds to <c>_listener.DisposeAsync()</c> while the orphaned <c>StopCoreAsync</c> is
/// still inside <c>_listener.StopAsync</c> — a dispose-before-stop-completes ordering violation
/// on the same transport.
///
/// The desired behaviour: DisposeAsync must wait for the in-flight stop to finish before
/// disposing the transport, so <c>_listener.StopAsync</c> and <c>_listener.DisposeAsync</c> never
/// overlap. The fix (leave <c>_stopTask</c> intact in the catch when a stop is in flight) makes
/// this test green.
///
/// Deterministic: a gated mock transport blocks inside StartAsync (released only by cancellation)
/// and blocks inside StopAsync, so the exact interleaving is forced via TCS barriers — no sleeps,
/// all waits bounded.
/// </summary>
public sealed class Round1_RpcHostStartStopRaceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task StopDuringStart_OrphanedStopCore_DoesNotLetDisposeOverlapTransportStop()
    {
        var transport = new GatedStartStopServerTransport();
        var host = RpcHost.Listen(transport, NewSerializer());

        // 1) StartAsync enters _listener.StartAsync and blocks (it only completes on cancellation).
        var startTask = host.StartAsync();
        await transport.StartEntered.WaitAsync(Timeout);

        // 2) StopAsync installs StopCoreAsync, which cancels cts (unblocking StartAsync into its
        //    throw path) and then parks inside _listener.StopAsync awaiting release.
        var stopTask = host.StopAsync();

        // 3) The cancelled StartAsync surfaces an OperationCanceledException via its catch block,
        //    which on the unfixed code nulls _cts/_stopTask and orphans the running StopCoreAsync.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => startTask.WaitAsync(Timeout));

        // 4) Ensure the orphaned StopCoreAsync has genuinely entered _listener.StopAsync, so an
        //    overlap with DisposeAsync would be observable.
        await transport.StopEntered.WaitAsync(Timeout);

        // 5) Dispose while the stop is still in flight. The transport records whether
        //    DisposeAsync was entered while a StopAsync was still running, then releases the stop
        //    so everything quiesces (bounded).
        await host.DisposeAsync().AsTask().WaitAsync(Timeout);

        // The orphaned stop must have finished without DisposeAsync overlapping it.
        await stopTask.WaitAsync(Timeout);

        Assert.False(
            transport.DisposeOverlappedStop,
            "DisposeAsync must wait for the in-flight StopAsync to finish before disposing the transport");
    }

    /// <summary>
    /// Mock transport that:
    ///  - blocks inside StartAsync until its token is cancelled (then throws OCE),
    ///  - blocks inside StopAsync until DisposeAsync releases it (so an overlap is observable),
    ///  - records whether DisposeAsync was entered while a StopAsync was still in flight.
    /// </summary>
    private sealed class GatedStartStopServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<bool> _startEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _stopEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowStopComplete =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stopInFlight;
        private int _disposeOverlappedStop;

        public Task StartEntered => _startEntered.Task;

        public Task StopEntered => _stopEntered.Task;

        public bool DisposeOverlappedStop => Volatile.Read(ref _disposeOverlappedStop) != 0;

        public async Task StartAsync(CancellationToken ct = default)
        {
            _startEntered.TrySetResult(true);

            // Block until cancelled; cancellation (from StopCoreAsync's cts.Cancel) surfaces as OCE,
            // driving StartAsync into its catch block.
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled))
            {
                await cancelled.Task.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("The accept loop should never start in this scenario.");

        public async Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref _stopInFlight, 1);
            _stopEntered.TrySetResult(true);
            try
            {
                // Park here so DisposeAsync can be observed overlapping the stop on the unfixed code.
                await _allowStopComplete.Task.ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _stopInFlight, 0);
            }
        }

        public ValueTask DisposeAsync()
        {
            // Record whether a StopAsync is still running at the moment dispose begins.
            if (Volatile.Read(ref _stopInFlight) != 0)
            {
                Interlocked.Exchange(ref _disposeOverlappedStop, 1);
            }

            // Release the parked stop so the orphaned StopCoreAsync can finish — keeps the test bounded.
            _allowStopComplete.TrySetResult(true);
            return default;
        }
    }
}
