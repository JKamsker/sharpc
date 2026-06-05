using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests;

public sealed class StreamingGeneratorTests
{
    [Fact]
    public void StreamingSignatures_GenerateStreamingProxyAndDispatcher()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Collections.Generic;
            using System.IO;
            using System.IO.Pipelines;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Streaming.Gen
            {
                [ShaRpcService]
                public interface IStreamingService
                {
                    IAsyncEnumerable<int> Numbers(CancellationToken ct = default);
                    Task<Stream> DownloadAsync(int id, CancellationToken ct = default);
                    ValueTask<Pipe> PipeAsync(CancellationToken ct = default);
                    Task<int> UploadAsync(Stream bytes, IAsyncEnumerable<int> items, Pipe pipe, CancellationToken ct = default);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("generated streaming code must compile");

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.GetText().ToString()));
        generated.Should().Contain("InvokeAsyncEnumerable<int>");
        generated.Should().Contain("InvokeStreamAsync<int>");
        generated.Should().Contain("InvokePipeAsync");
        generated.Should().Contain("ReserveStream(global::ShaRPC.Core.Protocol.RpcStreamKind.Binary)");
        generated.Should().Contain("ReserveStream(global::ShaRPC.Core.Protocol.RpcStreamKind.Items)");
        generated.Should().Contain("RpcStreamAttachment.FromStream");
        generated.Should().Contain("RpcStreamAttachment.FromAsyncEnumerable<int>");
        generated.Should().Contain("streaming.GetStream");
        generated.Should().Contain("streaming.GetAsyncEnumerable<int>");
        generated.Should().Contain("streaming.GetPipe");
        generated.Should().Contain("streaming.SetResponse(result)");
    }
}
