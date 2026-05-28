using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class RpcTypeValidator
{
    private const string ShaRpcServiceAttributeName = "ShaRPC.Core.Attributes.ShaRpcServiceAttribute";

    public static string? GetUnsupportedTypeReason(ITypeSymbol type, string role, CancellationToken ct)
    {
        if (ContainsRefLikeType(type, ct))
        {
            return $"{role} uses a ref-like type, which cannot be serialized as an RPC payload";
        }

        if (ContainsPointerType(type, ct))
        {
            return $"{role} uses a pointer type, which cannot be serialized as an RPC payload";
        }

        if (ContainsFunctionPointerType(type, ct))
        {
            return $"{role} uses a function pointer type, which cannot be serialized as an RPC payload";
        }

        return null;
    }

    public static string? GetUnsupportedSubServicePayloadReason(
        ITypeSymbol type,
        MethodReturnKind returnKind,
        string role,
        CancellationToken ct)
    {
        if (returnKind is MethodReturnKind.TaskOfSubService or MethodReturnKind.ValueTaskOfSubService)
        {
            return null;
        }

        return GetUnsupportedSubServicePayloadReason(type, role, ct);
    }

    public static string? GetUnsupportedSubServicePayloadReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct) =>
        ContainsShaRpcServiceInterface(type, ct, new HashSet<string>(System.StringComparer.Ordinal))
            ? $"{role} contains a sub-service type; sub-services are only supported as direct Task<TService> or ValueTask<TService> return values"
            : null;

    public static bool RequiresUnsafeContext(ITypeSymbol type, CancellationToken ct) =>
        ContainsPointerType(type, ct) || ContainsFunctionPointerType(type, ct);

    private static bool ContainsShaRpcServiceInterface(
        ITypeSymbol type,
        CancellationToken ct,
        HashSet<string> visited)
    {
        ct.ThrowIfCancellationRequested();

        if (type is INamedTypeSymbol named)
        {
            if (named.TypeKind == TypeKind.Interface && HasShaRpcServiceAttribute(named, ct))
            {
                return true;
            }

            var key = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!visited.Add(key))
            {
                return false;
            }

            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsShaRpcServiceInterface(arg, ct, visited))
                {
                    return true;
                }
            }

            if (CanInspectDtoMembers(named) && DtoMembersContainShaRpcServiceInterface(named, ct, visited))
            {
                return true;
            }
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsShaRpcServiceInterface(array.ElementType, ct, visited);
        }

        return false;
    }

    private static bool DtoMembersContainShaRpcServiceInterface(
        INamedTypeSymbol type,
        CancellationToken ct,
        HashSet<string> visited)
    {
        foreach (var member in type.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            var memberType = member switch
            {
                IPropertySymbol { IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public } property => property.Type,
                IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false, DeclaredAccessibility: Accessibility.Public } field => field.Type,
                _ => null,
            };

            if (memberType is not null && ContainsShaRpcServiceInterface(memberType, ct, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanInspectDtoMembers(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return false;
        }

        var ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace || !IsSystemNamespace(ns);
    }

    private static bool IsSystemNamespace(INamespaceSymbol ns)
    {
        while (!ns.IsGlobalNamespace)
        {
            if (ns.ContainingNamespace.IsGlobalNamespace)
            {
                return ns.Name == "System";
            }

            ns = ns.ContainingNamespace;
        }

        return false;
    }

    private static bool HasShaRpcServiceAttribute(INamedTypeSymbol type, CancellationToken ct)
    {
        foreach (var attr in type.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attr.AttributeClass?.ToDisplayString() == ShaRpcServiceAttributeName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRefLikeType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is INamedTypeSymbol named)
        {
            if (named.IsRefLikeType)
            {
                return true;
            }

            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsRefLikeType(arg, ct))
                {
                    return true;
                }
            }
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsRefLikeType(array.ElementType, ct);
        }

        return false;
    }

    private static bool ContainsPointerType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IPointerTypeSymbol)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsPointerType(array.ElementType, ct);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsPointerType(arg, ct))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsFunctionPointerType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IFunctionPointerTypeSymbol)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsFunctionPointerType(array.ElementType, ct);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsFunctionPointerType(arg, ct))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
