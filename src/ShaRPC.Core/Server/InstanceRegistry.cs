using System.Collections.Concurrent;

namespace ShaRPC.Core.Server;

/// <summary>
/// Default <see cref="IInstanceRegistry"/>. Backed by a single
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed on
/// <c>(serviceName, instanceId)</c>. One registry per connection.
/// </summary>
public sealed class InstanceRegistry : IInstanceRegistry
{
    private readonly ConcurrentDictionary<(string Service, string Id), object> _entries = new();

    /// <inheritdoc />
    public string Register(string serviceName, object instance)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        // Guid.NewGuid().ToString("N") is opaque enough on its own; per-connection scope
        // means an attacker on another connection cannot guess and reuse the id even if
        // they somehow learned it.
        var id = Guid.NewGuid().ToString("N");
        _entries[(serviceName, id)] = instance;
        return id;
    }

    /// <inheritdoc />
    public bool TryGet(string serviceName, string instanceId, out object instance)
    {
        if (_entries.TryGetValue((serviceName, instanceId), out var value))
        {
            instance = value;
            return true;
        }
        instance = null!;
        return false;
    }

    /// <inheritdoc />
    public void Release(string serviceName, string instanceId) =>
        _entries.TryRemove((serviceName, instanceId), out _);

    /// <inheritdoc />
    public void ReleaseAll() => _entries.Clear();
}
