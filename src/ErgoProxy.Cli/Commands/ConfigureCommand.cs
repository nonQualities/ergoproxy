using ErgoProxy.Core.Models;
using ErgoProxy.Core.Services;
using ErgoProxy.Core.Validation;

namespace ErgoProxy.Cli.Commands;

public static class ConfigureCommand
{
    public static async Task<int> ExecuteAsync(IProxyService proxyService, string[] args, CancellationToken ct = default)
    {
        string? name = null;
        string? host = null;
        int? port = null;
        var auth = false;
        string? username = null;
        string? password = null;
        var bypassRules = new List<string>();
        string? id = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--name" && i + 1 < args.Length) name = args[++i];
            else if (arg == "--host" && i + 1 < args.Length) host = args[++i];
            else if (arg == "--port" && i + 1 < args.Length && int.TryParse(args[++i], out var p)) port = p;
            else if (arg == "--auth") auth = true;
            else if (arg == "--username" && i + 1 < args.Length) { username = args[++i]; auth = true; }
            else if (arg == "--password" && i + 1 < args.Length) { password = args[++i]; auth = true; }
            else if (arg == "--bypass" && i + 1 < args.Length)
            {
                bypassRules = args[++i]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }
            else if (arg == "--id" && i + 1 < args.Length) id = args[++i];
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("Error: Missing required option '--name <name>'.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            Console.Error.WriteLine("Error: Missing required option '--host <host>'.");
            return 1;
        }

        if (!port.HasValue)
        {
            Console.Error.WriteLine("Error: Missing or invalid required option '--port <port>'.");
            return 1;
        }

        var profile = new ProxyProfile
        {
            Id = id ?? Guid.NewGuid().ToString("D"),
            Name = name,
            Host = host,
            Port = port.Value,
            AuthenticationEnabled = auth,
            CredentialReference = auth ? $"cred_{id ?? Guid.NewGuid().ToString("N")}" : null,
            BypassRules = bypassRules.Count > 0 ? bypassRules : new List<string> { "localhost", "127.0.0.1", "::1" }
        };

        var validator = new ProfileValidator();
        var validation = validator.Validate(profile);
        if (!validation.IsValid)
        {
            Console.Error.WriteLine($"Validation Error: {string.Join("; ", validation.Errors)}");
            return 1;
        }

        ProxyCredentials? creds = null;
        if (auth)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                Console.Error.WriteLine("Error: Both '--username' and '--password' are required when authentication is enabled.");
                return 1;
            }
            creds = new ProxyCredentials(username, password);
        }

        try
        {
            await proxyService.SaveProfileAsync(profile, creds, ct);
            await proxyService.SetActiveProfileAsync(profile.Id, ct);
            Console.WriteLine($"Successfully configured profile '{profile.Name}' ({profile.Host}:{profile.Port}) [ID: {profile.Id}].");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving profile: {ex.Message}");
            return 1;
        }
    }
}
