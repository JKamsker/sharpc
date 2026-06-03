using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Round 1 bug-hunt regression. A synchronous method whose name already ends in
/// <c>Async</c> AND carries a <see cref="System.Threading.CancellationToken"/> parameter
/// (e.g. <c>string GetDataAsync(CancellationToken ct)</c>) must still project onto the
/// async-sibling interface. The async-sibling projection key
/// (name + arity + parameter types) ignores <c>HasDefaultValue</c>, so the projected
/// sibling signature <c>GetDataAsync(CancellationToken ct = default)</c> hashes to the
/// SAME signature key as the original method — and the collision check in
/// <c>AsyncSiblingProjector.Compute</c> finds the method itself as the "blocker", emitting
/// a spurious SHARPC004 diagnostic and dropping the method from <c>IMyServiceAsync</c>.
///
/// Correct behaviour: a method must not collide with itself. No SHARPC004 should fire for
/// <c>GetDataAsync</c>, and the generated sibling interface must include a
/// <c>Task&lt;string&gt; GetDataAsync(...)</c> member.
/// </summary>
public sealed class Round1_AsyncSiblingSelfCollisionTests
{
    private const string Source = @"
using System.Threading;
using ShaRPC.Core.Attributes;

namespace Round1.SelfCollision
{
    [ShaRpcService]
    public interface IMyService
    {
        string GetDataAsync(CancellationToken ct);
    }
}";

    [Fact]
    public void SyncMethodNamedAsyncWithCancellationToken_DoesNotSelfCollide_AndProjectsOntoSibling()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        // (a) The method must not be reported as colliding with itself.
        var siblingCollisions = runResult.Diagnostics
            .Where(d => d.Id == "SHARPC004" && d.GetMessage().Contains("GetDataAsync"))
            .ToArray();
        Assert.Empty(siblingCollisions);

        // (b) The generated async-sibling interface must still contain the projected method,
        // returning Task<string>. Today the self-collision drops the only method, so the
        // sibling projection is empty and the .ShaRpcAsync.g.cs file is never emitted at all.
        var asyncSiblingTree = runResult.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("ShaRpcAsync"));
        Assert.True(
            asyncSiblingTree is not null,
            "the async-sibling interface file must be generated; the self-collision currently drops the only method and suppresses the file");

        // ReturnTypeClassifier renders the payload type with FullyQualifiedFormat, so string
        // surfaces as global::System.String.
        var asyncSibling = asyncSiblingTree!.GetText().ToString();
        Assert.Contains(
            "global::System.Threading.Tasks.Task<global::System.String> GetDataAsync(",
            asyncSibling);

        // Sanity: the whole thing still compiles.
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        Assert.True(
            emit.Success,
            string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
    }
}
