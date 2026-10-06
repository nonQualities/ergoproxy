namespace ErgoProxy.Core.Tunnel;

/// <summary>Original 4-tuple of an application TCP flow captured on the TUN device.</summary>
public readonly record struct FlowKey(uint SourceIp, ushort SourcePort, uint DestinationIp, ushort DestinationPort);

/// <summary>
/// Maps captured application flows to NAT ports used when the flow is re-injected towards the local relay.
/// The relay later recovers the original destination by looking up the NAT port of the accepted connection.
/// </summary>
public sealed class NatTable
{
    private sealed class Entry
    {
        public required FlowKey Key;
        public required ushort NatPort;
        public long LastSeenTicks;
        public bool RelayOwned;
        public long ReleasedTicks;
    }

    private readonly object _lock = new();
    private readonly Dictionary<FlowKey, Entry> _byKey = new();
    private readonly Entry?[] _byPort = new Entry?[65536];
    private readonly ushort _minPort;
    private readonly ushort _maxPort;
    private ushort _cursor;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _lingerAfterRelease;
    private readonly Func<long> _clock;

    public NatTable(ushort minPort = 10000, ushort maxPort = 65000, TimeSpan? idleTimeout = null,
        TimeSpan? lingerAfterRelease = null, Func<long>? clock = null)
    {
        _minPort = minPort;
        _maxPort = maxPort;
        _cursor = minPort;
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(2);
        _lingerAfterRelease = lingerAfterRelease ?? TimeSpan.FromSeconds(60);
        _clock = clock ?? (() => Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
    }

    public int Count
    {
        get { lock (_lock) return _byKey.Count; }
    }

    /// <summary>Returns the NAT port for a flow, creating a mapping when <paramref name="create"/> is set.</summary>
    public ushort? Lookup(FlowKey key, bool create)
    {
        lock (_lock)
        {
            var now = _clock();
            if (_byKey.TryGetValue(key, out var existing))
            {
                existing.LastSeenTicks = now;
                return existing.NatPort;
            }

            if (!create) return null;

            var range = _maxPort - _minPort + 1;
            for (var attempt = 0; attempt < range; attempt++)
            {
                var port = _cursor;
                _cursor = _cursor >= _maxPort ? _minPort : (ushort)(_cursor + 1);

                var occupant = _byPort[port];
                if (occupant != null)
                {
                    if (!IsExpired(occupant, now)) continue;
                    Remove(occupant);
                }

                var entry = new Entry { Key = key, NatPort = port, LastSeenTicks = now };
                _byKey[key] = entry;
                _byPort[port] = entry;
                return port;
            }

            return null; // table exhausted
        }
    }

    /// <summary>Reverse lookup used by both the relay (accept) and the engine (return path).</summary>
    public FlowKey? GetByNatPort(ushort natPort, bool touch = true)
    {
        lock (_lock)
        {
            var entry = _byPort[natPort];
            if (entry == null) return null;
            if (touch) entry.LastSeenTicks = _clock();
            return entry.Key;
        }
    }

    /// <summary>Marks a mapping as owned by a live relay connection so it never expires while in use.</summary>
    public void Acquire(ushort natPort)
    {
        lock (_lock)
        {
            var entry = _byPort[natPort];
            if (entry != null) entry.RelayOwned = true;
        }
    }

    /// <summary>Called when the relay connection ends; the mapping lingers briefly for trailing FIN/ACKs.</summary>
    public void Release(ushort natPort)
    {
        lock (_lock)
        {
            var entry = _byPort[natPort];
            if (entry == null) return;
            entry.RelayOwned = false;
            entry.ReleasedTicks = _clock();
        }
    }

    /// <summary>Removes expired mappings. Safe to call periodically.</summary>
    public int Sweep()
    {
        lock (_lock)
        {
            var now = _clock();
            var expired = _byKey.Values.Where(e => IsExpired(e, now)).ToList();
            foreach (var e in expired) Remove(e);
            return expired.Count;
        }
    }

    private bool IsExpired(Entry e, long now)
    {
        if (e.RelayOwned) return false;
        if (e.ReleasedTicks != 0) return now - e.ReleasedTicks > _lingerAfterRelease.Ticks;
        return now - e.LastSeenTicks > _idleTimeout.Ticks;
    }

    private void Remove(Entry e)
    {
        _byKey.Remove(e.Key);
        if (ReferenceEquals(_byPort[e.NatPort], e)) _byPort[e.NatPort] = null;
    }
}
