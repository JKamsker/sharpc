using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class ReviewedAsyncSiblingProjectionTests
{
    [Fact]
    public void GeneratedCtNameDisambiguation_CollidingWithUnsupportedOriginal_FiresSHARPC004()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;

            namespace AsyncSibling.K
            {
                [ShaRpcService]
                public interface IUnsupportedClash
                {
                    int Fetch(int ct);
                    ref int FetchAsync(int ct, CancellationToken ct1 = default);
                }
            }
            """;

        var (assembly, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC004" &&
            d.GetMessage().Contains("unsupported method 'FetchAsync'"));

        var proxy = assembly.GetType("AsyncSibling.K.UnsupportedClashProxy")!;
        proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FetchAsync")
            .Should().ContainSingle();
    }

    [Fact]
    public void VerbatimKeywordProjection_CollidingWithRegularAsyncName_FiresSHARPC004()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncSibling.KeywordCollision
            {
                [ShaRpcService]
                public interface IKeywords
                {
                    int @class();
                    Task<int> classAsync(CancellationToken ct = default);
                }
            }
            """;

        var (assembly, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC004" &&
            d.GetMessage().Contains("classAsync"));

        var proxy = assembly.GetType("AsyncSibling.KeywordCollision.KeywordsProxy")!;
        proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "classAsync")
            .Should().ContainSingle();
    }

    private static (Assembly Assembly, GeneratorDriverRunResult RunResult) Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
        ms.Position = 0;
        return (Assembly.Load(ms.ToArray()), runResult);
    }
}
