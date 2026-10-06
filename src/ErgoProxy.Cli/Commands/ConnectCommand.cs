using ErgoProxy.Cli.UI;
using ErgoProxy.Core.Services;

namespace ErgoProxy.Cli.Commands;

public static class ConnectCommand
{
    public static async Task<int> ExecuteAsync(IProxyService proxyService, string[] args, CancellationToken ct = default)
    {
        string? profileId = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--id" && i + 1 < args.Length)
            {
                profileId = args[++i];
            }
            else if (!arg.StartsWith('-') && profileId == null)
            {
                // Can be profile name or ID
                var profiles = await proxyService.GetProfilesAsync(ct);
                var matched = profiles.FirstOrDefault(p =>
                    string.Equals(p.Id, arg, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, arg, StringComparison.OrdinalIgnoreCase));

                profileId = matched?.Id ?? arg;
            }
        }

        var result = await proxyService.ConnectAsync(profileId, ct);

        if (result.Success)
        {
            Theme.ShowSuccess(result.Message);
            if (result.Status?.InterfaceName != null)
            {
                Console.WriteLine($"Tunnel Interface: {result.Status.InterfaceName}");
                Console.WriteLine($"Upstream Proxy:   {result.Status.ProxyEndpoint}");
                Console.WriteLine($"DNS Resolver:     {result.Status.DnsMode}");
            }
            return 0;
        }

        Theme.ShowError($"Tunnel activation failed: {result.Message}");
        return 1;
    }
}
