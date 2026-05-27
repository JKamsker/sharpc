namespace ShaRPC.SourceGenerator;

/// <summary>
/// Classifies the return shape of an RPC-facing method as declared on the user's interface.
/// </summary>
internal enum MethodReturnKind
{
    /// <summary><c>void</c></summary>
    Void,
    /// <summary>A non-<see cref="System.Threading.Tasks.Task"/> return — synchronous T.</summary>
    Sync,
    /// <summary>Non-generic <see cref="System.Threading.Tasks.Task"/> — async, no payload.</summary>
    Task,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> — async with payload.</summary>
    TaskOf,
}

/// <summary>
/// Immutable, value-equatable representation of a ShaRPC service.
/// </summary>
internal sealed record ServiceModel(
    string Namespace,
    string InterfaceName,
    string ServiceName,
    EquatableArray<MethodModel> Methods);

/// <summary>
/// Immutable, value-equatable representation of a service method.
/// </summary>
internal sealed record MethodModel(
    string Name,
    string RpcName,
    MethodReturnKind ReturnKind,
    string? UnwrappedReturnType,
    bool HasCancellationToken,
    EquatableArray<ParameterModel> Parameters);

/// <summary>
/// Immutable, value-equatable representation of a method parameter (excluding any
/// <see cref="System.Threading.CancellationToken"/>, which is tracked separately on the method).
/// </summary>
internal sealed record ParameterModel(string Name, string Type);

/// <summary>
/// Shared helpers used by both the proxy and dispatcher emitters.
/// </summary>
internal static class NamingHelpers
{
    /// <summary>
    /// Strips a leading <c>I</c> if it is followed by an uppercase letter (the C# convention
    /// for interface names). Avoids accidentally stripping the <c>I</c> from names like
    /// <c>Identity</c> or <c>Internal</c>.
    /// </summary>
    public static string StripInterfacePrefix(string interfaceName)
    {
        if (interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1]))
        {
            return interfaceName.Substring(1);
        }

        return interfaceName;
    }

    /// <summary>
    /// Reconstructs the literal return-type text as it would appear on the user's interface
    /// declaration, so the generated proxy signature exactly matches.
    /// </summary>
    public static string GetDeclaredReturnTypeText(MethodReturnKind kind, string? unwrappedReturnType)
    {
        return kind switch
        {
            MethodReturnKind.Void => "void",
            MethodReturnKind.Sync => unwrappedReturnType!,
            MethodReturnKind.Task => "Task",
            MethodReturnKind.TaskOf => $"Task<{unwrappedReturnType}>",
            _ => "void",
        };
    }
}
