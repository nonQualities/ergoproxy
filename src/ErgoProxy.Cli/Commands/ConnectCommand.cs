using ErgoProxy.Cli.UI;
using ErgoProxy.Core.Services;

namespace ErgoProxy.Cli.Commands;

public static class ConnectCommand
{
    public static async Task<int> ExecuteAsync(IProxyService proxyService, string[] args, CancellationToken ct = default)
    {
        string? profileId = null;
        var force = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--id" && i + 1 < args.Length) profileId = args[++i];
            else if (arg == "--force" || arg == "-f") force = true;
        }

        var result = await proxyService.ConnectAsync(profileId, force, ct);

        if (result.Success)
        {
            Theme.ShowSuccess(result.Message);
            return 0;
        }

        if (result.HasConflict)
        {
            Theme.ShowWarning($"Conflict: {result.Message}");
            return 3;
        }

        Theme.ShowError($"Activation failed: {result.Message}");
        return 1;
    }
}
