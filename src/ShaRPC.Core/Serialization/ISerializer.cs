namespace ShaRPC.Core.Serialization;

/// <summary>
/// Abstraction for message serialization.
/// </summary>
public interface ISerializer
{
    /// <summary>
    /// Serializes a value to a byte array.
    /// </summary>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// Deserializes a value from a byte span.
    /// </summary>
    T Deserialize<T>(ReadOnlySpan<byte> data);

    /// <summary>
    /// Deserializes a value from a byte span to a specified type.
    /// </summary>
    object? Deserialize(ReadOnlySpan<byte> data, Type type);
}
