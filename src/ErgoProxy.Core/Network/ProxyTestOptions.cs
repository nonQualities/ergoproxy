namespace ErgoProxy.Core.Network;

public sealed class ProxyTestOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public string HttpTestUrl { get; set; } = "http://connectivitycheck.gstatic.com/generate_204";
    public string HttpsTestHost { get; set; } = "www.google.com";
    public int HttpsTestPort { get; set; } = 443;
    public bool TestHttpsConnectTunnel { get; set; } = true;
    public bool ValidateTlsCertificate { get; set; } = true;

    public static ProxyTestOptions Default => new();
}
