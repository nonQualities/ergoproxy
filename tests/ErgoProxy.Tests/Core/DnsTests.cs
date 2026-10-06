using System.Buffers.Binary;
using ErgoProxy.Core.Tunnel;
using ErgoProxy.Core.Tunnel.Dns;
using ErgoProxy.Core.Tunnel.Packets;
using Xunit;

namespace ErgoProxy.Tests.Core;

public class DnsTests
{
    [Fact]
    public void DnsMessage_SerializationRoundtrip()
    {
        var msg = new DnsMessage
        {
            Id = 0x1234,
            IsResponse = false,
            RecursionDesired = true
        };
        msg.Questions.Add(new DnsQuestion
        {
            Name = "example.com",
            Type = 1,
            Class = 1
        });

        var bytes = msg.ToBytes();
        Assert.True(DnsMessage.TryParse(bytes, out var parsed));
        Assert.Equal(0x1234, parsed.Id);
        Assert.Single(parsed.Questions);
        Assert.Equal("example.com", parsed.Questions[0].Name);
        Assert.Equal(1, parsed.Questions[0].Type);
    }

    [Fact]
    public void FakeIpPool_AllocatesAndResolvesConsistently()
    {
        var addressing = new TunnelAddressing();
        var pool = new FakeIpPool(addressing);

        var ip1 = pool.AllocateOrGet("campus.portal.edu");
        Assert.True(pool.IsFakeIp(ip1));

        var ip2 = pool.AllocateOrGet("campus.portal.edu");
        Assert.Equal(ip1, ip2);

        var host = pool.ResolveHost(ip1);
        Assert.Equal("campus.portal.edu", host);

        var ipOther = pool.AllocateOrGet("google.com");
        Assert.NotEqual(ip1, ipOther);
        Assert.Equal("google.com", pool.ResolveHost(ipOther));
    }

    [Fact]
    public void FakeDnsServer_HandlesAQueryWithFakeIp_AndAAAAWithNoData()
    {
        var addressing = new TunnelAddressing();
        var pool = new FakeIpPool(addressing);
        var server = new FakeDnsServer(pool);

        // 1. Query 'A' for blocked.site.org
        var queryA = new DnsMessage { Id = 101, RecursionDesired = true };
        queryA.Questions.Add(new DnsQuestion { Name = "blocked.site.org", Type = 1, Class = 1 });

        var respABytes = server.HandleQuery(queryA.ToBytes());
        Assert.NotNull(respABytes);
        Assert.True(DnsMessage.TryParse(respABytes, out var respA));
        Assert.True(respA.IsResponse);
        Assert.Equal(0, respA.ResponseCode); // NoError
        Assert.Single(respA.Answers);
        Assert.Equal(1, respA.Answers[0].Type);

        var fakeIpUint = BinaryPrimitives.ReadUInt32BigEndian(respA.Answers[0].Data);
        Assert.True(pool.IsFakeIp(fakeIpUint));
        Assert.Equal("blocked.site.org", pool.ResolveHost(fakeIpUint));

        // 2. Query 'AAAA' for blocked.site.org -> Should return 0 answers (NODATA)
        var queryAaaa = new DnsMessage { Id = 102, RecursionDesired = true };
        queryAaaa.Questions.Add(new DnsQuestion { Name = "blocked.site.org", Type = 28, Class = 1 });

        var respAaaaBytes = server.HandleQuery(queryAaaa.ToBytes());
        Assert.NotNull(respAaaaBytes);
        Assert.True(DnsMessage.TryParse(respAaaaBytes, out var respAaaa));
        Assert.Equal(0, respAaaa.ResponseCode);
        Assert.Empty(respAaaa.Answers); // Forces fallback to IPv4
    }
}
