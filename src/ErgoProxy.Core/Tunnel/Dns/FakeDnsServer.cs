using System.Buffers.Binary;
using System.Net;
using ErgoProxy.Core.Tunnel.Packets;

namespace ErgoProxy.Core.Tunnel.Dns;

public sealed class FakeDnsServer
{
    private readonly FakeIpPool _pool;
    private readonly HashSet<string> _bypassDomains = new(StringComparer.OrdinalIgnoreCase);
    private string? _proxyHost;

    public FakeDnsServer(FakeIpPool pool)
    {
        _pool = pool;
    }

    public void SetProxyHost(string? host)
    {
        _proxyHost = host?.Trim().TrimEnd('.');
    }

    public void SetBypassDomains(IEnumerable<string> domains)
    {
        _bypassDomains.Clear();
        foreach (var d in domains)
        {
            var clean = d.Trim().TrimEnd('.');
            if (!string.IsNullOrEmpty(clean))
            {
                _bypassDomains.Add(clean);
            }
        }
    }

    public byte[]? HandleQuery(ReadOnlySpan<byte> queryBytes)
    {
        if (!DnsMessage.TryParse(queryBytes, out var query) || query.IsResponse)
        {
            return null;
        }

        var response = new DnsMessage
        {
            Id = query.Id,
            IsResponse = true,
            OpCode = query.OpCode,
            AuthoritativeAnswer = false,
            RecursionDesired = query.RecursionDesired,
            RecursionAvailable = true,
            ResponseCode = 0 // NoError
        };

        foreach (var q in query.Questions)
        {
            response.Questions.Add(q);

            var qName = q.Name.Trim().TrimEnd('.');

            // Type 1: A record (IPv4)
            if (q.Type == 1)
            {
                // If it is proxy host or in bypass list, resolve to real IP if possible
                if (IsDirectDomain(qName))
                {
                    try
                    {
                        var addrs = System.Net.Dns.GetHostAddresses(qName);
                        var v4 = addrs.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                        if (v4 != null)
                        {
                            response.Answers.Add(new DnsRecord
                            {
                                Name = q.Name,
                                Type = 1,
                                Class = q.Class,
                                Ttl = 60,
                                Data = v4.GetAddressBytes()
                            });
                            continue;
                        }
                    }
                    catch
                    {
                        // Fallback to fake IP if real resolution fails
                    }
                }

                // Allocate or retrieve Fake IP
                var fakeIp = _pool.AllocateOrGet(qName);
                var ipBytes = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(ipBytes, fakeIp);

                response.Answers.Add(new DnsRecord
                {
                    Name = q.Name,
                    Type = 1,
                    Class = q.Class,
                    Ttl = 5, // Short TTL so client doesn't hold stale mapping across restarts
                    Data = ipBytes
                });
            }
            // Type 28: AAAA (IPv6) or Type 65: HTTPS (SVCB/HTTPS)
            else if (q.Type is 28 or 65)
            {
                // Return empty response (NODATA) to force fallback to IPv4 and standard TCP
                // ResponseCode is already NoError
            }
            // Type 12: PTR (Reverse DNS)
            else if (q.Type == 12 && qName.EndsWith(".in-addr.arpa", StringComparison.OrdinalIgnoreCase))
            {
                var ptrIp = ParseInAddrArpa(qName);
                if (ptrIp.HasValue && _pool.ResolveHost(ptrIp.Value) is { } host)
                {
                    // Encode host as DNS name bytes
                    using var ms = new MemoryStream();
                    using var writer = new BinaryWriter(ms);
                    foreach (var part in host.Split('.'))
                    {
                        var partBytes = System.Text.Encoding.ASCII.GetBytes(part);
                        writer.Write((byte)partBytes.Length);
                        writer.Write(partBytes);
                    }
                    writer.Write((byte)0);

                    response.Answers.Add(new DnsRecord
                    {
                        Name = q.Name,
                        Type = 12,
                        Class = q.Class,
                        Ttl = 60,
                        Data = ms.ToArray()
                    });
                }
            }
            else
            {
                // For other types, return NODATA (NoError with 0 answers)
            }
        }

        return response.ToBytes();
    }

    private bool IsDirectDomain(string domain)
    {
        if (_proxyHost != null && string.Equals(domain, _proxyHost, StringComparison.OrdinalIgnoreCase))
            return true;

        if (_bypassDomains.Contains(domain))
            return true;

        foreach (var bp in _bypassDomains)
        {
            if (bp.StartsWith("*.") && domain.EndsWith(bp[1..], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static uint? ParseInAddrArpa(string name)
    {
        var parts = name.Split('.');
        if (parts.Length < 6) return null; // 4 octets + in-addr + arpa
        if (byte.TryParse(parts[0], out var b4) &&
            byte.TryParse(parts[1], out var b3) &&
            byte.TryParse(parts[2], out var b2) &&
            byte.TryParse(parts[3], out var b1))
        {
            return ((uint)b1 << 24) | ((uint)b2 << 16) | ((uint)b3 << 8) | b4;
        }
        return null;
    }
}
