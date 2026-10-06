using System.Text.RegularExpressions;

namespace ErgoProxy.Core.Security;

public static class SecretRedactor
{
    private static readonly Regex UrlCredentialsRegex = new(
        @"(https?://)([^:]+):([^@]+)@",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ProxyAuthHeaderRegex = new(
        @"(Proxy-Authorization\s*:\s*Basic\s+)[A-Za-z0-9+/=]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BasicAuthHeaderRegex = new(
        @"(Authorization\s*:\s*Basic\s+)[A-Za-z0-9+/=]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PasswordCliParamRegex = new(
        @"(--password|-p)\s+([^\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PasswordJsonRegex = new(
        @"(""password""\s*:\s*"")([^""]+)("")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex KeyValuePasswordRegex = new(
        @"(password|passwd|pass|pwd)\s*[:=]\s*([^\s,;""']+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = text;
        result = UrlCredentialsRegex.Replace(result, "$1$2:***@");
        result = ProxyAuthHeaderRegex.Replace(result, "$1***");
        result = BasicAuthHeaderRegex.Replace(result, "$1***");
        result = PasswordCliParamRegex.Replace(result, "$1 ***");
        result = PasswordJsonRegex.Replace(result, "$1***$3");
        result = KeyValuePasswordRegex.Replace(result, "$1: ***");

        return result;
    }

    public static string RedactUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        return UrlCredentialsRegex.Replace(url, "$1$2:***@");
    }
}
