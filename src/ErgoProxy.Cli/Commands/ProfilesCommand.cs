using System.Text.Json;
using ErgoProxy.Cli.UI;
using ErgoProxy.Core.Services;

namespace ErgoProxy.Cli.Commands;

public static class ProfilesCommand
{
    public static async Task<int> ExecuteAsync(IProxyService proxyService, string[] args, CancellationToken ct = default)
    {
        var asJson = args.Contains("--json");
        var profiles = await proxyService.GetProfilesAsync(ct);
        var active = await proxyService.GetActiveProfileAsync(ct);

        if (asJson)
        {
            var json = JsonSerializer.Serialize(new
            {
                active_profile_id = active?.Id,
                profiles
            }, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            return 0;
        }

        StatusRenderer.RenderProfiles(profiles, active?.Id);
        return 0;
    }
}
