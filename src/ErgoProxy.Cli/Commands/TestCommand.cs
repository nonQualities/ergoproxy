using System.Text.Json;
using ErgoProxy.Cli.UI;
using ErgoProxy.Core.Network;
using ErgoProxy.Core.Services;

namespace ErgoProxy.Cli.Commands;

public static class TestCommand
{
    public static async Task<int> ExecuteAsync(IProxyService proxyService, string[] args, CancellationToken ct = default)
    {
        string? profileId = null;
        string? endpoint = null;
        var timeoutSec = 5;
        var asJson = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--id" && i + 1 < args.Length) profileId = args[++i];
            else if (arg == "--endpoint" && i + 1 < args.Length) endpoint = args[++i];
            else if (arg == "--timeout" && i + 1 < args.Length && int.TryParse(args[++i], out var t)) timeoutSec = t;
            else if (arg == "--json") asJson = true;
        }

        var options = new ProxyTestOptions
        {
            Timeout = TimeSpan.FromSeconds(timeoutSec)
        };
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            options.HttpTestUrl = endpoint;
        }

        var result = await proxyService.TestProfileAsync(profileId, options, ct);

        if (asJson)
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
        }
        else
        {
            StatusRenderer.RenderTestResult(result);
        }

        if (result.IsSuccess) return 0;
        if (result.ErrorCode == Core.Models.ProxyTestErrorCode.InvalidConfig) return 1;
        return 2; // Network or proxy connectivity failure
    }
}
