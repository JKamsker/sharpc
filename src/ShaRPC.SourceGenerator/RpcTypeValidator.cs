using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class RpcTypeValidator
{
    public static string? GetUnsupportedTypeReason(ITypeSymbol type, string role)
    {
        if (ContainsRefLikeType(type))
        {
            return $"{role} uses a ref-like type, which cannot be serialized as an RPC payload";
        }

        if (ContainsPointerType(type))
        {
            return $"{role} uses a pointer type, which cannot be serialized as an RPC payload";
        }

        return null;
    }

    private static bool ContainsRefLikeType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named)
        {
            if (named.IsRefLikeType)
            {
                return true;
            }

            foreach (var arg in named.TypeArguments)
            {
                if (ContainsRefLikeType(arg))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsPointerType(ITypeSymbol type)
    {
        if (type is IPointerTypeSymbol)
        {
            return true;
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var arg in named.TypeArguments)
            {
                if (ContainsPointerType(arg))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
