using System.Text.Json;
using ErgoProxy.Cli.UI;
using ErgoProxy.Core.Services;

namespace ErgoProxy.Cli.Commands;

public static class StatusCommand
{
    public static async Task<int> ExecuteAsync(IProxyService proxyService, string[] args, CancellationToken ct = default)
    {
        var asJson = args.Contains("--json");
        var status = await proxyService.GetStatusAsync(ct);

        if (asJson)
        {
            var json = JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            return 0;
        }

        StatusRenderer.RenderStatus(status);
        return 0;
    }
}
