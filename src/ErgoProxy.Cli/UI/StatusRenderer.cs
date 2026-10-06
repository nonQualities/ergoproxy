using ErgoProxy.Core.Models;
using ErgoProxy.Core.Services;
using Spectre.Console;

namespace ErgoProxy.Cli.UI;

public static class StatusRenderer
{
    public static void RenderStatus(ProxyStatusInfo status)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn(new GridColumn());

        // Profile info
        if (status.ActiveProfile != null)
        {
            grid.AddRow("[bold]Selected Profile:[/]", $"[cyan]{Markup.Escape(status.ActiveProfile.Name)}[/] [dim]({status.ActiveProfile.Id})[/]");
            grid.AddRow("[bold]Proxy Endpoint:[/]", $"[white]{status.ActiveProfile.Host}:{status.ActiveProfile.Port}[/]");
            grid.AddRow("[bold]Authentication:[/]", status.ActiveProfile.AuthenticationEnabled ? "[green]Enabled (OS Vault)[/]" : "[grey]Disabled[/]");
            grid.AddRow("[bold]Bypass Rules:[/]", status.ActiveProfile.BypassRules.Count > 0 ? string.Join(", ", status.ActiveProfile.BypassRules) : "[grey]None[/]");
        }
        else
        {
            grid.AddRow("[bold]Selected Profile:[/]", "[dim yellow]None selected[/]");
        }

        // Platform integration
        var platformStatus = status.PlatformSupported ? $"[green]{status.PlatformName}[/]" : $"[red]{status.PlatformName} (Unsupported)[/]";
        grid.AddRow("[bold]Platform Adapter:[/]", platformStatus);

        // System proxy status
        var managedStatus = status.IsSystemProxyApplied
            ? "[bold green]Applied by ErgoProxy[/]"
            : "[dim grey]Not applied[/]";
        grid.AddRow("[bold]Managed Proxy:[/]", managedStatus);

        var osStatus = status.IsSystemProxyActuallyActive
            ? "[bold green]Active in OS Settings[/]"
            : "[dim grey]Disabled in OS Settings[/]";
        grid.AddRow("[bold]OS Proxy State:[/]", osStatus);

        // Last test result
        if (status.LastTestResult != null)
        {
            var testBadge = status.LastTestResult.IsSuccess
                ? $"[green]Verified ({status.LastTestResult.LatencyMilliseconds:F1}ms)[/]"
                : $"[red]Failed ({status.LastTestResult.Stage} / {status.LastTestResult.ErrorCode})[/]";
            var testTime = status.LastTestedAt?.ToLocalTime().ToString("g") ?? "Recently";
            grid.AddRow("[bold]Connectivity:[/]", $"{testBadge} [dim]at {testTime}[/]");
            if (!status.LastTestResult.IsSuccess)
            {
                grid.AddRow("[bold]Error Detail:[/]", $"[red]{Markup.Escape(status.LastTestResult.Message)}[/]");
            }
        }
        else
        {
            grid.AddRow("[bold]Connectivity:[/]", "[grey]Not tested yet[/]");
        }

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("[bold cyan] Connection Status [/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1, 2, 1)
        };

        AnsiConsole.Write(panel);

        if (status.HasConflict)
        {
            AnsiConsole.WriteLine();
            var conflictPanel = new Panel(new Markup($"[bold red]WARNING:[/] {Markup.Escape(status.ConflictMessage ?? "External conflict detected.")}"))
            {
                Border = BoxBorder.Heavy,
                BorderStyle = new Style(Color.Yellow)
            };
            AnsiConsole.Write(conflictPanel);
        }

        if (status.HasPendingRecovery)
        {
            AnsiConsole.WriteLine();
            var recoveryPanel = new Panel(new Markup($"[bold red]RECOVERY ALERT:[/] {Markup.Escape(status.RecoveryMessage ?? "Pending recovery state detected.")}\n[dim]Run 'disconnect' or restore OS proxy manually if necessary.[/]"))
            {
                Border = BoxBorder.Heavy,
                BorderStyle = new Style(Color.Red)
            };
            AnsiConsole.Write(recoveryPanel);
        }
    }

    public static void RenderTestResult(ProxyTestResult result)
    {
        var statusBadge = result.IsSuccess 
            ? "[bold green]PASS[/]" 
            : "[bold red]FAIL[/]";

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("[bold]Check[/]");
        table.AddColumn("[bold]Status[/]");
        table.AddColumn("[bold]Details[/]");

        table.AddRow("Result", statusBadge, result.IsSuccess ? "[green]Proxy connection verified[/]" : $"[red]{Markup.Escape(result.Message)}[/]");
        table.AddRow("Stage", $"[cyan]{result.Stage}[/]", result.IsSuccess ? "[green]Completed[/]" : $"[yellow]{result.ErrorCode}[/]");
        table.AddRow("Latency", $"[white]{result.LatencyMilliseconds:F1} ms[/]", "-");
        
        if (result.HttpStatusCode.HasValue)
        {
            table.AddRow("HTTP Code", $"[white]{result.HttpStatusCode}[/]", "-");
        }

        table.AddRow("HTTPS CONNECT", result.ConnectTunnelSuccess ? "[green]Verified[/]" : "[grey]Skipped / Failed[/]", "-");

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[bold cyan] Connectivity Test Report [/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
    }

    public static void RenderProfiles(IReadOnlyList<ProxyProfile> profiles, string? activeProfileId)
    {
        if (profiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No proxy profiles configured yet. Create one to get started![/]");
            return;
        }

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("[bold]Active[/]");
        table.AddColumn("[bold]ID[/]");
        table.AddColumn("[bold]Name[/]");
        table.AddColumn("[bold]Endpoint[/]");
        table.AddColumn("[bold]Auth[/]");
        table.AddColumn("[bold]Bypass[/]");
        table.AddColumn("[bold]Modified[/]");

        foreach (var p in profiles)
        {
            var isActive = string.Equals(p.Id, activeProfileId, StringComparison.OrdinalIgnoreCase);
            var activeMarker = isActive ? "[bold green]● ACTIVE[/]" : "[dim grey]○[/]";
            var auth = p.AuthenticationEnabled ? "[green]Yes[/]" : "[grey]No[/]";
            var bypass = p.BypassRules.Count > 0 ? $"{p.BypassRules.Count} rules" : "[grey]None[/]";
            var modified = p.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            table.AddRow(
                activeMarker,
                $"[dim]{p.Id[..Math.Min(8, p.Id.Length)]}...[/]",
                $"[bold white]{Markup.Escape(p.Name)}[/]",
                $"{p.Host}:{p.Port}",
                auth,
                bypass,
                modified);
        }

        AnsiConsole.Write(table);
    }
}
