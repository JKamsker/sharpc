using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ShaRPC.SourceGenerator;

internal sealed record ExistingTypeIndex(EquatableArray<ExistingTypeInfo> Types)
{
    public static ExistingTypeIndex Create(ImmutableArray<ExistingTypeInfo> types, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return new ExistingTypeIndex(types
            .OrderBy(static type => type, ExistingTypeInfoComparer.Instance)
            .ToEquatableArray());
    }

    public ExistingTypeInfo? Find(string @namespace, string name, CancellationToken ct)
    {
        var target = new ExistingTypeInfo(@namespace, name, default);
        var low = 0;
        var high = Types.Count - 1;
        while (low <= high)
        {
            ct.ThrowIfCancellationRequested();

            var mid = low + ((high - low) / 2);
            var comparison = ExistingTypeInfoComparer.Instance.Compare(Types[mid], target);
            if (comparison == 0)
            {
                return Types[mid];
            }

            if (comparison < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return null;
    }

    public static ExistingTypeInfo? FromDeclaration(SyntaxNode node)
    {
        if (!TryGetTypeName(node, out var name) || IsNestedInType(node) || IsFileLocal(node))
        {
            return null;
        }

        return new ExistingTypeInfo(
            GetNamespace(node),
            name,
            DiagnosticLocation.FromLocation(GetNameLocation(node)));
    }

    private static bool TryGetTypeName(SyntaxNode node, out string name)
    {
        switch (node)
        {
            case BaseTypeDeclarationSyntax declaration:
                name = declaration.Identifier.ValueText;
                return true;
            case DelegateDeclarationSyntax declaration:
                name = declaration.Identifier.ValueText;
                return true;
            default:
                name = string.Empty;
                return false;
        }
    }

    private static Location? GetNameLocation(SyntaxNode node) =>
        node switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Identifier.GetLocation(),
            DelegateDeclarationSyntax declaration => declaration.Identifier.GetLocation(),
            _ => null,
        };

    private static bool IsNestedInType(SyntaxNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFileLocal(SyntaxNode node)
    {
        var modifiers = node switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Modifiers,
            DelegateDeclarationSyntax declaration => declaration.Modifiers,
            _ => default,
        };

        foreach (var modifier in modifiers)
        {
            if (modifier.IsKind(SyntaxKind.FileKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var namespaces = new List<string>();
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                namespaces.Add(namespaceDeclaration.Name.ToString().Replace("@", string.Empty));
            }
        }

        namespaces.Reverse();
        return string.Join(".", namespaces);
    }
}

internal readonly record struct ExistingTypeInfo(
    string Namespace,
    string Name,
    DiagnosticLocation Location);

internal sealed class ExistingTypeInfoComparer : IComparer<ExistingTypeInfo>
{
    public static ExistingTypeInfoComparer Instance { get; } = new();

    public int Compare(ExistingTypeInfo left, ExistingTypeInfo right)
    {
        var ns = string.Compare(left.Namespace, right.Namespace, System.StringComparison.Ordinal);
        return ns != 0
            ? ns
            : string.Compare(left.Name, right.Name, System.StringComparison.Ordinal);
    }
}
