using ErgoProxy.Core.Models;
using Spectre.Console;
using SpectreValidation = Spectre.Console.ValidationResult;

namespace ErgoProxy.Cli.UI;

public static class ProfilePrompts
{
    public static (ProxyProfile Profile, ProxyCredentials? Credentials) PromptCreateProfile()
    {
        AnsiConsole.MarkupLine("[bold cyan]─── Create New Proxy Profile ───[/]\n");

        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("Profile Name:")
                .Validate(n => string.IsNullOrWhiteSpace(n) 
                    ? SpectreValidation.Error("Name cannot be empty.") 
                    : SpectreValidation.Success()));

        var host = AnsiConsole.Prompt(
            new TextPrompt<string>("Proxy Hostname or IP:")
                .Validate(h => string.IsNullOrWhiteSpace(h) 
                    ? SpectreValidation.Error("Host cannot be empty.") 
                    : SpectreValidation.Success()));

        var port = AnsiConsole.Prompt(
            new TextPrompt<int>("Proxy TCP Port:")
                .DefaultValue(8080)
                .Validate(p => p is >= 1 and <= 65535 
                    ? SpectreValidation.Success() 
                    : SpectreValidation.Error("Port must be between 1 and 65535.")));

        var enableAuth = AnsiConsole.Confirm("Enable proxy authentication (Username & Password)?", defaultValue: false);

        ProxyCredentials? credentials = null;
        string? credRef = null;

        if (enableAuth)
        {
            var user = AnsiConsole.Prompt(
                new TextPrompt<string>("Proxy Username:")
                    .Validate(u => string.IsNullOrWhiteSpace(u) 
                        ? SpectreValidation.Error("Username cannot be empty.") 
                        : SpectreValidation.Success()));

            var pass = AnsiConsole.Prompt(
                new TextPrompt<string>("Proxy Password:")
                    .Secret()
                    .Validate(p => string.IsNullOrWhiteSpace(p) 
                        ? SpectreValidation.Error("Password cannot be empty.") 
                        : SpectreValidation.Success()));

            credRef = $"cred_{Guid.NewGuid():N}";
            credentials = new ProxyCredentials(user, pass);
        }

        var bypassInput = AnsiConsole.Prompt(
            new TextPrompt<string>("Bypass rules (comma-separated, or press Enter for default localhost):")
                .AllowEmpty()
                .DefaultValue("localhost, 127.0.0.1, ::1"));

        var bypassRules = bypassInput
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var profile = new ProxyProfile
        {
            Name = name.Trim(),
            Host = host.Trim(),
            Port = port,
            AuthenticationEnabled = enableAuth,
            CredentialReference = credRef,
            BypassRules = bypassRules
        };

        return (profile, credentials);
    }

    public static (ProxyProfile Profile, ProxyCredentials? Credentials) PromptEditProfile(ProxyProfile original, ProxyCredentials? existingCreds)
    {
        AnsiConsole.MarkupLine($"[bold cyan]─── Edit Profile: {Markup.Escape(original.Name)} ───[/]\n");

        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("Profile Name:")
                .DefaultValue(original.Name));

        var host = AnsiConsole.Prompt(
            new TextPrompt<string>("Proxy Hostname or IP:")
                .DefaultValue(original.Host));

        var port = AnsiConsole.Prompt(
            new TextPrompt<int>("Proxy TCP Port:")
                .DefaultValue(original.Port)
                .Validate(p => p is >= 1 and <= 65535 
                    ? SpectreValidation.Success() 
                    : SpectreValidation.Error("Port must be between 1 and 65535.")));

        var enableAuth = AnsiConsole.Confirm("Enable proxy authentication?", defaultValue: original.AuthenticationEnabled);

        ProxyCredentials? credentials = existingCreds;
        var credRef = original.CredentialReference;

        if (enableAuth)
        {
            var updateCreds = existingCreds == null || AnsiConsole.Confirm("Update stored credentials?", defaultValue: false);
            if (updateCreds)
            {
                var user = AnsiConsole.Prompt(
                    new TextPrompt<string>("Proxy Username:")
                        .DefaultValue(existingCreds?.Username ?? string.Empty));

                var pass = AnsiConsole.Prompt(
                    new TextPrompt<string>("Proxy Password:")
                        .Secret());

                credRef ??= $"cred_{original.Id}";
                credentials = new ProxyCredentials(user, pass);
            }
        }
        else
        {
            credentials = null;
        }

        var currentBypass = string.Join(", ", original.BypassRules);
        var bypassInput = AnsiConsole.Prompt(
            new TextPrompt<string>("Bypass rules (comma-separated):")
                .AllowEmpty()
                .DefaultValue(currentBypass));

        var bypassRules = bypassInput
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var edited = original.Clone();
        edited.Name = name.Trim();
        edited.Host = host.Trim();
        edited.Port = port;
        edited.AuthenticationEnabled = enableAuth;
        edited.CredentialReference = enableAuth ? credRef : null;
        edited.BypassRules = bypassRules;
        edited.ModifiedAt = DateTimeOffset.UtcNow;

        return (edited, credentials);
    }
}
