using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Server;

/// <summary>
/// Interface for generated service dispatchers that route incoming requests
/// to the appropriate service method.
/// </summary>
public interface IServiceDispatcher
{
    /// <summary>
    /// Gets the name of the service this dispatcher handles.
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Dispatches a request to the appropriate method and returns the serialized result.
    /// </summary>
    Task<byte[]> DispatchAsync(string method, byte[] payload, ISerializer serializer, CancellationToken ct = default);
}
