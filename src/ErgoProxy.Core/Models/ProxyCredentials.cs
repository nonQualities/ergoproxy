namespace ErgoProxy.Core.Models;

public sealed class ProxyCredentials
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public ProxyCredentials() { }

    public ProxyCredentials(string username, string password)
    {
        Username = username;
        Password = password;
    }

    public override string ToString()
    {
        return $"[Username={Username}, Password=***]";
    }
}
