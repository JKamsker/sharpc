using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator.Tests;

public class GeneratedTypeCollisionTests
{
    [Fact]
    public void ExistingProxyType_ProducesSHARPC003_AtCollidingType()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.GeneratedTypeCollision
            {
                public sealed class FooProxy
                {
                }

                [ShaRpcService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        var diagnostic = runResult.Diagnostics.Single(d => d.Id == "SHARPC003");
        diagnostic.GetMessage().Should().Contain("generated proxy type 'FooProxy' would collide");
        DiagnosticText(source, diagnostic).Should().Contain("FooProxy");
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    [Fact]
    public void ExistingDispatcherType_ProducesSHARPC003_AndServiceIsSkipped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.GeneratedTypeCollision
            {
                public sealed class FooDispatcher
                {
                }

                [ShaRpcService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("generated dispatcher type 'FooDispatcher' would collide"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    [Fact]
    public void ExistingGeneratedExtensionsType_ProducesSHARPC003_AndServicesAreSkipped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace ShaRPC.Generated
            {
                public static class ShaRpcGeneratedExtensions
                {
                }
            }

            namespace Regress.GeneratedTypeCollision
            {
                [ShaRpcService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        var diagnostic = runResult.Diagnostics.Single(d => d.Id == "SHARPC003");
        diagnostic.GetMessage().Should().Contain(
            "generated extension type 'ShaRPC.Generated.ShaRpcGeneratedExtensions' would collide");
        DiagnosticText(source, diagnostic).Should().Contain("ShaRpcGeneratedExtensions");
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    private static GeneratorDriverRunResult Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        return driver.GetRunResult();
    }

    private static string DiagnosticText(string source, Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var line = source.Replace("\r\n", "\n").Split('\n')[span.StartLinePosition.Line];
        return line.Substring(span.StartLinePosition.Character);
    }
}
