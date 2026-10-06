using ErgoProxy.Cli.UI;
using ErgoProxy.Core.Services;

namespace ErgoProxy.Cli.Commands;

public static class DisconnectCommand
{
    public static async Task<int> ExecuteAsync(IProxyService proxyService, string[] args, CancellationToken ct = default)
    {
        var result = await proxyService.DisconnectAsync(ct);

        if (result.Success)
        {
            Theme.ShowSuccess(result.Message);
            return 0;
        }

        Theme.ShowError($"Disconnect failed: {result.Message}");
        return 1;
    }
}
