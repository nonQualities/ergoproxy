using ErgoProxy.Core.Models;
using ErgoProxy.Core.Services;
using ErgoProxy.Core.Tunnel;
using ErgoProxy.Core.Tunnel.Platform;
using Spectre.Console;

namespace ErgoProxy.Cli.UI;

public sealed class InteractiveMenu
{
    private readonly IProxyService _proxyService;

    public InteractiveMenu(IProxyService proxyService)
    {
        _proxyService = proxyService;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            AnsiConsole.Clear();
            Theme.RenderHeader();

            var status = await _proxyService.GetStatusAsync(ct);
            StatusRenderer.RenderStatus(status);

            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Main Menu Options:[/]")
                    .PageSize(10)
                    .HighlightStyle(new Style(Color.Cyan1))
                    .AddChoices(new[]
                    {
                        "⚡ Connect (Start Device-Wide Tunnel)",
                        "🔌 Disconnect (Stop Tunnel & Restore Routes)",
                        "📋 Profile Management",
                        "🔍 Test Proxy Connectivity",
                        "📊 View Detailed Status & Protocols",
                        "🔑 Manage Credentials",
                        "🛠 Network Diagnostics & Recovery",
                        "❓ Help & Documentation",
                        "🚪 Exit Application"
                    }));

            try
            {
                switch (choice)
                {
                    case "⚡ Connect (Start Device-Wide Tunnel)":
                        await HandleConnectAsync(status, ct);
                        break;
                    case "🔌 Disconnect (Stop Tunnel & Restore Routes)":
                        await HandleDisconnectAsync(ct);
                        break;
                    case "📋 Profile Management":
                        await HandleProfilesAsync(ct);
                        break;
                    case "🔍 Test Proxy Connectivity":
                        await HandleTestAsync(status, ct);
                        break;
                    case "📊 View Detailed Status & Protocols":
                        await HandleViewStatusAsync(ct);
                        break;
                    case "🔑 Manage Credentials":
                        await HandleCredentialsAsync(status, ct);
                        break;
                    case "🛠 Network Diagnostics & Recovery":
                        await HandleDiagnosticsAsync(status, ct);
                        break;
                    case "❓ Help & Documentation":
                        ShowHelp();
                        break;
                    case "🚪 Exit Application":
                        AnsiConsole.MarkupLine("\n[cyan]Goodbye![/]");
                        return;
                }
            }
            catch (Exception ex)
            {
                Theme.ShowError($"An unexpected error occurred: {ex.Message}");
            }

            AnsiConsole.MarkupLine("\n[dim]Press any key to continue...[/]");
            Console.ReadKey(intercept: true);
        }
    }

    private async Task HandleConnectAsync(ProxyStatusInfo status, CancellationToken ct)
    {
        if (status.ActiveProfile == null)
        {
            Theme.ShowWarning("No proxy profile selected. Please create or select a profile first.");
            return;
        }

        AnsiConsole.WriteLine();
        var confirm = AnsiConsole.Confirm(
            $"Establish device-wide tunnel through [cyan]{Markup.Escape(status.ActiveProfile.Name)}[/] ({status.ActiveProfile.Host}:{status.ActiveProfile.Port})?",
            defaultValue: true);

        if (!confirm) return;

        TunnelOperationResult result = null!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Establishing tunnel and configuring system routing...", async _ =>
            {
                result = await _proxyService.ConnectAsync(status.ActiveProfile.Id, ct);
            });

        if (result.Success)
        {
            Theme.ShowSuccess(result.Message);
            if (result.Status?.InterfaceName != null)
            {
                AnsiConsole.MarkupLine($"[green]Tunnel Device:[/] [white]{result.Status.InterfaceName}[/]");
                AnsiConsole.MarkupLine($"[green]DNS Mode:[/]      [white]{result.Status.DnsMode}[/]");
            }
        }
        else
        {
            Theme.ShowError(result.Message);
        }
    }

    private async Task HandleDisconnectAsync(CancellationToken ct)
    {
        AnsiConsole.WriteLine();
        var confirm = AnsiConsole.Confirm("Stop tunnel and restore default network routing?", defaultValue: true);
        if (!confirm) return;

        TunnelOperationResult result = null!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Restoring system networking...", async _ =>
            {
                result = await _proxyService.DisconnectAsync(ct);
            });

        if (result.Success)
        {
            Theme.ShowSuccess(result.Message);
        }
        else
        {
            Theme.ShowError(result.Message);
        }
    }

    private async Task HandleProfilesAsync(CancellationToken ct)
    {
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Profile Actions:[/]")
                .AddChoices(new[]
                {
                    "Select Active Profile",
                    "Create New Profile",
                    "Edit Profile",
                    "Delete Profile",
                    "Back to Main Menu"
                }));

        var profiles = await _proxyService.GetProfilesAsync(ct);

        switch (action)
        {
            case "Select Active Profile":
                if (profiles.Count == 0)
                {
                    Theme.ShowWarning("No profiles available.");
                    return;
                }
                var selectedName = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a profile to activate:")
                        .AddChoices(profiles.Select(p => $"{p.Name} ({p.Host}:{p.Port}) [{p.Id}]")));

                var selId = selectedName.Split('[', ']').Where(s => s.Contains('-') || s.Length >= 8).LastOrDefault();
                if (selId != null)
                {
                    await _proxyService.SetActiveProfileAsync(selId, ct);
                    Theme.ShowSuccess($"Active profile set to: {selectedName}");
                }
                break;

            case "Create New Profile":
                var (newProfile, creds) = ProfilePrompts.PromptCreateProfile();
                await _proxyService.SaveProfileAsync(newProfile, creds, ct);
                await _proxyService.SetActiveProfileAsync(newProfile.Id, ct);
                Theme.ShowSuccess($"Profile '{newProfile.Name}' created and set as active!");
                break;

            case "Edit Profile":
                if (profiles.Count == 0)
                {
                    Theme.ShowWarning("No profiles available to edit.");
                    return;
                }
                var editChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Choose profile to edit:")
                        .AddChoices(profiles.Select(p => $"{p.Name} [{p.Id}]")));

                var editId = editChoice.Split('[', ']').Where(s => s.Contains('-') || s.Length >= 8).LastOrDefault();
                var target = profiles.FirstOrDefault(p => p.Id == editId);
                if (target != null)
                {
                    ProxyCredentials? existingCreds = null;
                    if (target.AuthenticationEnabled && target.CredentialReference != null)
                    {
                        existingCreds = await _proxyService.GetCredentialsAsync(target.CredentialReference, ct);
                    }
                    var (editedProfile, updatedCreds) = ProfilePrompts.PromptEditProfile(target, existingCreds);
                    await _proxyService.SaveProfileAsync(editedProfile, updatedCreds, ct);
                    Theme.ShowSuccess($"Profile '{editedProfile.Name}' updated successfully.");
                }
                break;

            case "Delete Profile":
                if (profiles.Count == 0)
                {
                    Theme.ShowWarning("No profiles available.");
                    return;
                }
                var delChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Choose profile to delete:")
                        .AddChoices(profiles.Select(p => $"{p.Name} [{p.Id}]")));

                var delId = delChoice.Split('[', ']').Where(s => s.Contains('-') || s.Length >= 8).LastOrDefault();
                if (delId != null && AnsiConsole.Confirm($"Delete profile '{delChoice}'?", defaultValue: false))
                {
                    await _proxyService.DeleteProfileAsync(delId, ct);
                    Theme.ShowSuccess("Profile deleted.");
                }
                break;
        }
    }

    private async Task HandleTestAsync(ProxyStatusInfo status, CancellationToken ct)
    {
        if (status.ActiveProfile == null)
        {
            Theme.ShowWarning("No profile selected for testing. Please configure a profile first.");
            return;
        }

        AnsiConsole.MarkupLine($"\n[bold cyan]Testing connectivity for profile:[/] {Markup.Escape(status.ActiveProfile.Name)} ({status.ActiveProfile.Host}:{status.ActiveProfile.Port})...\n");

        ProxyTestResult result = null!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .StartAsync("Running diagnostic test pipeline (DNS -> TCP -> HTTP -> CONNECT -> TLS)...", async _ =>
            {
                result = await _proxyService.TestProfileAsync(status.ActiveProfile.Id, options: null, ct);
            });

        StatusRenderer.RenderTestResult(result);
    }

    private async Task HandleViewStatusAsync(CancellationToken ct)
    {
        AnsiConsole.Clear();
        Theme.RenderHeader();
        var status = await _proxyService.GetStatusAsync(ct);
        StatusRenderer.RenderStatus(status);

        AnsiConsole.WriteLine();
        StatusRenderer.RenderProtocols(status.TunnelStatus.Protocols);

        var profiles = await _proxyService.GetProfilesAsync(ct);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]Configured Profiles:[/]");
        StatusRenderer.RenderProfiles(profiles, status.ActiveProfile?.Id);
    }

    private async Task HandleCredentialsAsync(ProxyStatusInfo status, CancellationToken ct)
    {
        if (status.ActiveProfile == null)
        {
            Theme.ShowWarning("No active profile selected.");
            return;
        }

        var p = status.ActiveProfile;
        AnsiConsole.MarkupLine($"\n[bold]Credentials for profile:[/] [cyan]{Markup.Escape(p.Name)}[/]");

        if (!p.AuthenticationEnabled)
        {
            Theme.ShowInfo("Authentication is currently disabled for this profile.");
            if (AnsiConsole.Confirm("Enable authentication now?", defaultValue: true))
            {
                var user = AnsiConsole.Prompt(new TextPrompt<string>("Username:"));
                var pass = AnsiConsole.Prompt(new TextPrompt<string>("Password:").Secret());
                p.AuthenticationEnabled = true;
                p.CredentialReference ??= $"cred_{p.Id}";
                await _proxyService.SaveProfileAsync(p, new ProxyCredentials(user, pass), ct);
                Theme.ShowSuccess("Authentication enabled and credentials saved to secure vault.");
            }
            return;
        }

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Credential Options:")
                .AddChoices(new[] { "Update Username/Password", "Disable Authentication", "Cancel" }));

        if (action == "Update Username/Password")
        {
            var user = AnsiConsole.Prompt(new TextPrompt<string>("New Username:"));
            var pass = AnsiConsole.Prompt(new TextPrompt<string>("New Password:").Secret());
            p.CredentialReference ??= $"cred_{p.Id}";
            await _proxyService.SaveProfileAsync(p, new ProxyCredentials(user, pass), ct);
            Theme.ShowSuccess("Credentials updated securely.");
        }
        else if (action == "Disable Authentication")
        {
            p.AuthenticationEnabled = false;
            await _proxyService.SaveProfileAsync(p, null, ct);
            Theme.ShowSuccess("Authentication disabled for this profile.");
        }
    }

    private async Task HandleDiagnosticsAsync(ProxyStatusInfo status, CancellationToken ct)
    {
        AnsiConsole.MarkupLine("\n[bold cyan]─── Network Diagnostics & Recovery ───[/]\n");
        AnsiConsole.MarkupLine($"Operating System: [white]{Environment.OSVersion}[/]");
        AnsiConsole.MarkupLine($"Tunnel State:     [white]{status.TunnelStatus.State}[/]");
        AnsiConsole.MarkupLine($"Upstream Status:  {(status.TunnelStatus.UpstreamReachable ? "[green]Reachable[/]" : "[yellow]Unreachable / Degraded[/]")}");

        if (status.HasPendingRecovery)
        {
            AnsiConsole.MarkupLine($"Recovery Alert:   [bold red]{status.RecoveryMessage}[/]");
        }

        if (AnsiConsole.Confirm("Attempt automatic cleanup (reset TUN interface, routing rules, and DNS)?", defaultValue: false))
        {
            var platform = new LinuxTunnelPlatform();
            await platform.CleanStaleStateAsync(new TunnelAddressing(), ct);
            await _proxyService.DisconnectAsync(ct);
            Theme.ShowSuccess("Network cleanup complete. System routing restored.");
        }
    }

    private static void ShowHelp()
    {
        AnsiConsole.Clear();
        Theme.RenderHeader();

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("[bold]Feature / Concept[/]");
        table.AddColumn("[bold]Description & Behavior[/]");

        table.AddRow("Device-Wide Tunnel", "ErgoProxy creates a local TUN interface and configures OS policy routing so application network traffic is captured system-wide without configuring individual apps.");
        table.AddRow("Upstream HTTP Proxy", "Captured TCP flows are transported through your campus or remote HTTP proxy using standard HTTP CONNECT tunnels.");
        table.AddRow("Zero TLS Interception", "Application TLS sessions remain encrypted end-to-end between your device and the destination. No custom root CA certificates or payload inspection.");
        table.AddRow("Fake-IP DNS Engine", "Virtual DNS resolver intercepts domain lookups and returns fake IPs, delegating destination resolution entirely to the upstream proxy to bypass campus DNS censorship.");
        table.AddRow("Reversible Networking", "Disconnecting removes policy routing rules, table routes, and restores DNS so your network state returns cleanly to default.");
        table.AddRow("Credential Vault", "Proxy credentials are stored securely in the OS keyring or an encrypted AES vault, and never appear in plaintext logs or diagnostics.");

        AnsiConsole.Write(table);
    }
}
