using System;
using System.Collections.Generic;

namespace ShaRPC.SourceGenerator;

/// <summary>
/// Represents information about a ShaRPC service extracted from the source.
/// </summary>
internal sealed class ServiceInfo
{
    public string Namespace { get; set; } = string.Empty;
    public string InterfaceName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public List<MethodInfo> Methods { get; set; } = new();
}

/// <summary>
/// Represents information about a method in a ShaRPC service.
/// </summary>
internal sealed class MethodInfo
{
    public string Name { get; set; } = string.Empty;
    public string RpcName { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public string? UnwrappedReturnType { get; set; }
    public bool ReturnsTask { get; set; }
    public bool ReturnsVoid { get; set; }
    public List<ParameterInfo> Parameters { get; set; } = new();
}

/// <summary>
/// Represents information about a parameter in an RPC method.
/// </summary>
internal sealed class ParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
