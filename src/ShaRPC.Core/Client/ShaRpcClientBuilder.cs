using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Client;

/// <summary>
/// Builder for configuring and creating a ShaRPC client.
/// </summary>
public sealed class ShaRpcClientBuilder
{
    private ITransport? _transport;
    private ISerializer? _serializer;
    private TimeSpan _timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sets the transport for the client.
    /// </summary>
    public ShaRpcClientBuilder UseTransport(ITransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        return this;
    }

    /// <summary>
    /// Sets the serializer for the client.
    /// </summary>
    public ShaRpcClientBuilder UseSerializer(ISerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        return this;
    }

    /// <summary>
    /// Sets the default request timeout.
    /// </summary>
    public ShaRpcClientBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Builds the client instance.
    /// </summary>
    public ShaRpcClient Build()
    {
        if (_transport == null)
        {
            throw new InvalidOperationException("Transport must be configured.");
        }

        if (_serializer == null)
        {
            throw new InvalidOperationException("Serializer must be configured.");
        }

        return new ShaRpcClient(_transport, _serializer, _timeout);
    }
}
