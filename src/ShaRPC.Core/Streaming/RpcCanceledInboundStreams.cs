using ShaRPC.Core.Buffers;

namespace ShaRPC.Core.Streaming;

internal sealed class RpcCanceledInboundStreams
{
    internal const int Capacity = 1024;

    private readonly object _gate = new();
    private readonly LinkedList<int> _order = new();
    private readonly Dictionary<int, LinkedListNode<int>> _streamIds = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _streamIds.Count;
            }
        }
    }

    internal int TrackingCount
    {
        get
        {
            lock (_gate)
            {
                return _order.Count;
            }
        }
    }

    public void Add(int streamId)
    {
        lock (_gate)
        {
            if (_streamIds.ContainsKey(streamId))
            {
                return;
            }

            var node = _order.AddLast(streamId);
            _streamIds.Add(streamId, node);
            Trim();
        }
    }

    public bool TryConsumeItem(int streamId, Payload frame)
    {
        if (!Contains(streamId))
        {
            return false;
        }

        frame.Dispose();
        return true;
    }

    public bool TryRemove(int streamId)
    {
        lock (_gate)
        {
            return RemoveLocked(streamId);
        }
    }

    public bool Contains(int streamId)
    {
        lock (_gate)
        {
            return _streamIds.ContainsKey(streamId);
        }
    }

    public void Remove(int streamId)
    {
        lock (_gate)
        {
            RemoveLocked(streamId);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _streamIds.Clear();
            _order.Clear();
        }
    }

    private bool RemoveLocked(int streamId)
    {
        if (!_streamIds.Remove(streamId, out var node))
        {
            return false;
        }

        _order.Remove(node);
        return true;
    }

    private void Trim()
    {
        while (_streamIds.Count > Capacity)
        {
            var first = _order.First;
            if (first is null)
            {
                return;
            }

            _order.RemoveFirst();
            _streamIds.Remove(first.Value);
        }
    }
}
