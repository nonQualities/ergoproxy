using System.Net;
using ErgoProxy.Core.Tunnel.Packets;

namespace ErgoProxy.Core.Tunnel.Dns;

public sealed class FakeIpPool
{
    private readonly object _lock = new();
    private readonly uint _startIp;
    private readonly uint _endIp;
    private uint _currentIp;

    private readonly Dictionary<string, uint> _hostToIp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, string> _ipToHost = new();

    public FakeIpPool(TunnelAddressing addressing)
    {
        // Default 198.19.0.0/16 range
        var baseIp = IPv4.ToUInt32(addressing.FakeIpBase);
        _startIp = baseIp + 1; // 198.19.0.1
        _endIp = baseIp + 0xFFFF - 1; // 198.19.255.254
        _currentIp = _startIp;
    }

    public uint AllocateOrGet(string host)
    {
        lock (_lock)
        {
            var cleanHost = host.Trim().TrimEnd('.');
            if (_hostToIp.TryGetValue(cleanHost, out var existing))
            {
                return existing;
            }

            // Simple circular allocation with slot eviction if full
            var allocated = _currentIp;
            _currentIp = (_currentIp >= _endIp) ? _startIp : _currentIp + 1;

            if (_ipToHost.TryGetValue(allocated, out var oldHost))
            {
                _hostToIp.Remove(oldHost);
            }

            _hostToIp[cleanHost] = allocated;
            _ipToHost[allocated] = cleanHost;
            return allocated;
        }
    }

    public string? ResolveHost(uint ip)
    {
        lock (_lock)
        {
            return _ipToHost.TryGetValue(ip, out var host) ? host : null;
        }
    }

    public bool IsFakeIp(uint ip)
    {
        return ip >= _startIp && ip <= _endIp;
    }
}
