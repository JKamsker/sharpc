using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ShaRPC.SourceGenerator;

/// <summary>
/// Incremental source generator for ShaRPC client proxies and server dispatchers.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ShaRpcGenerator : IIncrementalGenerator
{
    private const string ShaRpcServiceAttributeName = "ShaRPC.Core.Attributes.ShaRpcServiceAttribute";
    private const string ShaRpcMethodAttributeName = "ShaRPC.Core.Attributes.ShaRpcMethodAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all interface declarations with [ShaRpcService] attribute
        var serviceInterfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsInterfaceWithAttribute(s),
                transform: static (ctx, ct) => GetServiceInfo(ctx, ct))
            .Where(static s => s is not null)
            .Select(static (s, _) => s!);

        // Register source output
        context.RegisterSourceOutput(serviceInterfaces, static (spc, service) =>
        {
            // Generate proxy
            var proxySource = ProxyGenerator.Generate(service);
            var proxyFileName = $"{service.InterfaceName}.ShaRpcProxy.g.cs";
            spc.AddSource(proxyFileName, SourceText.From(proxySource, Encoding.UTF8));

            // Generate dispatcher
            var dispatcherSource = DispatcherGenerator.Generate(service);
            var dispatcherFileName = $"{service.InterfaceName}.ShaRpcDispatcher.g.cs";
            spc.AddSource(dispatcherFileName, SourceText.From(dispatcherSource, Encoding.UTF8));
        });

        // Collect all services and generate extensions
        var allServices = serviceInterfaces.Collect();
        context.RegisterSourceOutput(allServices, static (spc, services) =>
        {
            if (services.IsEmpty)
            {
                return;
            }

            var extensionsSource = GenerateExtensions(services);
            spc.AddSource("ShaRpcExtensions.g.cs", SourceText.From(extensionsSource, Encoding.UTF8));
        });
    }

    private static bool IsInterfaceWithAttribute(SyntaxNode node)
    {
        return node is InterfaceDeclarationSyntax interfaceDeclaration
            && interfaceDeclaration.AttributeLists.Count > 0;
    }

    private static ServiceInfo? GetServiceInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var interfaceDeclaration = (InterfaceDeclarationSyntax)context.Node;

        // Check if it has the ShaRpcService attribute
        var symbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration, ct);
        if (symbol is not INamedTypeSymbol interfaceSymbol)
        {
            return null;
        }

        var hasShaRpcAttribute = interfaceSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == ShaRpcServiceAttributeName);

        if (!hasShaRpcAttribute)
        {
            return null;
        }

        // Get custom service name if specified
        var attr = interfaceSymbol.GetAttributes()
            .First(a => a.AttributeClass?.ToDisplayString() == ShaRpcServiceAttributeName);

        string? customName = null;
        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key == "Name" && namedArg.Value.Value is string name)
            {
                customName = name;
            }
        }

        var service = new ServiceInfo
        {
            Namespace = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : interfaceSymbol.ContainingNamespace.ToDisplayString(),
            InterfaceName = interfaceSymbol.Name,
            ServiceName = customName ?? interfaceSymbol.Name
        };

        // Get all methods
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is IMethodSymbol methodSymbol && methodSymbol.MethodKind == MethodKind.Ordinary)
            {
                // Check for custom method name
                string? customMethodName = null;
                var methodAttr = methodSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ShaRpcMethodAttributeName);

                if (methodAttr != null)
                {
                    foreach (var namedArg in methodAttr.NamedArguments)
                    {
                        if (namedArg.Key == "Name" && namedArg.Value.Value is string name)
                        {
                            customMethodName = name;
                        }
                    }
                }

                var returnType = methodSymbol.ReturnType;
                var returnsTask = returnType.Name == "Task" ||
                    (returnType is INamedTypeSymbol namedReturn && namedReturn.ConstructedFrom?.Name == "Task");

                string returnTypeStr;
                string? unwrappedReturnType = null;
                bool returnsVoid = false;

                if (returnType is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.Name == "Task")
                {
                    // Task<T>
                    unwrappedReturnType = namedType.TypeArguments[0].ToDisplayString();
                    returnTypeStr = $"Task<{unwrappedReturnType}>";
                }
                else if (returnType.Name == "Task")
                {
                    // Task (void)
                    returnTypeStr = "Task";
                    returnsVoid = true;
                }
                else
                {
                    // Sync method - wrap in Task
                    returnTypeStr = returnType.SpecialType == SpecialType.System_Void
                        ? "Task"
                        : $"Task<{returnType.ToDisplayString()}>";
                    unwrappedReturnType = returnType.SpecialType == SpecialType.System_Void
                        ? null
                        : returnType.ToDisplayString();
                    returnsVoid = returnType.SpecialType == SpecialType.System_Void;
                }

                var method = new MethodInfo
                {
                    Name = methodSymbol.Name,
                    RpcName = customMethodName ?? methodSymbol.Name,
                    ReturnType = returnTypeStr,
                    UnwrappedReturnType = unwrappedReturnType,
                    ReturnsTask = returnsTask,
                    ReturnsVoid = returnsVoid
                };

                // Get parameters (excluding CancellationToken)
                foreach (var param in methodSymbol.Parameters)
                {
                    if (param.Type.ToDisplayString() != "System.Threading.CancellationToken")
                    {
                        method.Parameters.Add(new ParameterInfo
                        {
                            Name = param.Name,
                            Type = param.Type.ToDisplayString()
                        });
                    }
                }

                service.Methods.Add(method);
            }
        }

        return service;
    }

    private static string GenerateExtensions(ImmutableArray<ServiceInfo> services)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using ShaRPC.Core.Client;");
        sb.AppendLine("using ShaRPC.Core.Server;");
        sb.AppendLine();
        sb.AppendLine("namespace ShaRPC.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Extension methods for registering generated ShaRPC services.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class ShaRpcGeneratedExtensions");
        sb.AppendLine("    {");

        foreach (var service in services)
        {
            var serviceName = service.InterfaceName.StartsWith("I")
                ? service.InterfaceName.Substring(1)
                : service.InterfaceName;
            var proxyName = serviceName + "Proxy";
            var dispatcherName = serviceName + "Dispatcher";
            var fullInterfaceName = string.IsNullOrEmpty(service.Namespace)
                ? service.InterfaceName
                : $"{service.Namespace}.{service.InterfaceName}";
            var fullProxyName = string.IsNullOrEmpty(service.Namespace)
                ? proxyName
                : $"{service.Namespace}.{proxyName}";
            var fullDispatcherName = string.IsNullOrEmpty(service.Namespace)
                ? dispatcherName
                : $"{service.Namespace}.{dispatcherName}";

            // Create proxy extension
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Creates a proxy for {service.InterfaceName}.");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        public static {fullInterfaceName} Create{serviceName}Proxy(this IShaRpcClient client)");
            sb.AppendLine($"            => new {fullProxyName}(client);");

            // Add service extension for builder
            sb.AppendLine();
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Registers {service.InterfaceName} with the server.");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        public static ShaRpcServerBuilder Add{serviceName}(this ShaRpcServerBuilder builder, {fullInterfaceName} implementation)");
            sb.AppendLine($"            => builder.AddDispatcher(new {fullDispatcherName}(implementation));");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
