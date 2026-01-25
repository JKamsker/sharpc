using MessagePack;
using MessagePack.Resolvers;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Serializers.MessagePack;

/// <summary>
/// MessagePack-based serializer implementation.
/// </summary>
public sealed class MessagePackRpcSerializer : ISerializer
{
    private readonly MessagePackSerializerOptions _options;

    /// <summary>
    /// Creates a new MessagePack serializer with default options.
    /// </summary>
    public MessagePackRpcSerializer() : this(CreateDefaultOptions())
    {
    }

    /// <summary>
    /// Creates a new MessagePack serializer with custom options.
    /// </summary>
    public MessagePackRpcSerializer(MessagePackSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Creates a MessagePack serializer optimized for Unity compatibility.
    /// Uses ContractlessStandardResolver which doesn't require attributes.
    /// </summary>
    public static MessagePackRpcSerializer CreateUnityCompatible()
    {
        var options = MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithSecurity(MessagePackSecurity.UntrustedData);

        return new MessagePackRpcSerializer(options);
    }

    private static MessagePackSerializerOptions CreateDefaultOptions()
    {
        return MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
                StandardResolver.Instance,
                ContractlessStandardResolver.Instance))
            .WithSecurity(MessagePackSecurity.UntrustedData);
    }

    public byte[] Serialize<T>(T value)
    {
        return MessagePackSerializer.Serialize(value, _options);
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data)
    {
        return MessagePackSerializer.Deserialize<T>(data.ToArray(), _options);
    }

    public object? Deserialize(ReadOnlySpan<byte> data, Type type)
    {
        return MessagePackSerializer.Deserialize(type, data.ToArray(), _options);
    }
}
