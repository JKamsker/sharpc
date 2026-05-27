using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal sealed record ExistingTypeIndex(EquatableArray<ExistingTypeInfo> Types)
{
    public static ExistingTypeIndex Create(Compilation compilation, CancellationToken ct)
    {
        var types = new List<ExistingTypeInfo>();
        CollectNamespaceTypes(compilation.Assembly.GlobalNamespace, types, ct);

        return new ExistingTypeIndex(types
            .OrderBy(static type => type.Namespace, System.StringComparer.Ordinal)
            .ThenBy(static type => type.Name, System.StringComparer.Ordinal)
            .ToEquatableArray());
    }

    public ExistingTypeInfo? Find(string @namespace, string name, CancellationToken ct)
    {
        foreach (var type in Types.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (type.Namespace == @namespace && type.Name == name)
            {
                return type;
            }
        }

        return null;
    }

    private static void CollectNamespaceTypes(
        INamespaceSymbol namespaceSymbol,
        List<ExistingTypeInfo> types,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();

            var ns = type.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : type.ContainingNamespace.ToDisplayString();
            types.Add(new ExistingTypeInfo(ns, type.Name, DiagnosticLocationFactory.FromSymbol(type)));
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            CollectNamespaceTypes(childNamespace, types, ct);
        }
    }
}

internal readonly record struct ExistingTypeInfo(
    string Namespace,
    string Name,
    DiagnosticLocation Location);
