using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class RpcTypeValidator
{
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

        return null;
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

        return false;
    }

    private static bool ContainsPointerType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IPointerTypeSymbol)
        {
            return true;
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
}
