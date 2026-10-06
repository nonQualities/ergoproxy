using ErgoProxy.Cli.UI;
using ErgoProxy.Core.Services;

namespace ErgoProxy.Cli.Commands;

public static class DisconnectCommand
{
    public static async Task<int> ExecuteAsync(IProxyService proxyService, string[] args, CancellationToken ct = default)
    {
        var force = args.Contains("--force") || args.Contains("-f");

        var result = await proxyService.DisconnectAsync(force, ct);

        if (result.Success)
        {
            Theme.ShowSuccess(result.Message);
            return 0;
        }

        Theme.ShowError($"Deactivation failed: {result.Message}");
        return 3;
    }
}
