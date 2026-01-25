using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Server;

/// <summary>
/// Builder for configuring and creating a ShaRPC server.
/// </summary>
public sealed class ShaRpcServerBuilder
{
    private IServerTransport? _transport;
    private ISerializer? _serializer;
    private readonly List<IServiceDispatcher> _dispatchers = new();
    private readonly List<Func<IServiceProvider?, IServiceDispatcher>> _dispatcherFactories = new();

    /// <summary>
    /// Sets the transport for the server.
    /// </summary>
    public ShaRpcServerBuilder UseTransport(IServerTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        return this;
    }

    /// <summary>
    /// Sets the serializer for the server.
    /// </summary>
    public ShaRpcServerBuilder UseSerializer(ISerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        return this;
    }

    /// <summary>
    /// Registers a service dispatcher directly.
    /// </summary>
    public ShaRpcServerBuilder AddDispatcher(IServiceDispatcher dispatcher)
    {
        _dispatchers.Add(dispatcher ?? throw new ArgumentNullException(nameof(dispatcher)));
        return this;
    }

    /// <summary>
    /// Registers a service with an implementation instance.
    /// </summary>
    public ShaRpcServerBuilder AddService<TService, TDispatcher>(TService implementation)
        where TDispatcher : IServiceDispatcher
    {
        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(typeof(TDispatcher), implementation)!;
        _dispatchers.Add(dispatcher);
        return this;
    }

    /// <summary>
    /// Registers a dispatcher factory for dependency injection scenarios.
    /// </summary>
    public ShaRpcServerBuilder AddDispatcherFactory(Func<IServiceProvider?, IServiceDispatcher> factory)
    {
        _dispatcherFactories.Add(factory ?? throw new ArgumentNullException(nameof(factory)));
        return this;
    }

    /// <summary>
    /// Builds the server instance.
    /// </summary>
    public ShaRpcServer Build(IServiceProvider? serviceProvider = null)
    {
        if (_transport == null)
        {
            throw new InvalidOperationException("Transport must be configured.");
        }

        if (_serializer == null)
        {
            throw new InvalidOperationException("Serializer must be configured.");
        }

        var server = new ShaRpcServer(_transport, _serializer);

        foreach (var dispatcher in _dispatchers)
        {
            server.RegisterDispatcher(dispatcher);
        }

        foreach (var factory in _dispatcherFactories)
        {
            var dispatcher = factory(serviceProvider);
            server.RegisterDispatcher(dispatcher);
        }

        return server;
    }
}
