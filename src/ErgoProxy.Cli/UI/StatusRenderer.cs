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
            grid.AddRow("[bold]Authentication:[/]", status.ActiveProfile.AuthenticationEnabled ? "[green]Enabled (OS Keyring / Vault)[/]" : "[grey]Disabled[/]");
            grid.AddRow("[bold]Bypass Rules:[/]", status.ActiveProfile.BypassRules.Count > 0 ? string.Join(", ", status.ActiveProfile.BypassRules) : "[grey]None[/]");
        }
        else
        {
            grid.AddRow("[bold]Selected Profile:[/]", "[dim yellow]None selected[/]");
        }

        // Tunnel state badge
        var t = status.TunnelStatus;
        var stateColor = t.State switch
        {
            TunnelState.Connected => "bold green",
            TunnelState.Degraded => "bold yellow",
            TunnelState.Connecting or TunnelState.Validating => "bold cyan",
            TunnelState.Disconnecting => "bold yellow",
            TunnelState.Error => "bold red",
            _ => "dim grey"
        };
        var stateBadge = $"[{stateColor}]{t.State.ToString().ToUpperInvariant()}[/]";
        grid.AddRow("[bold]Tunnel State:[/]", stateBadge);

        if (t.IsActive)
        {
            grid.AddRow("[bold]Tunnel Interface:[/]", $"[white]{t.InterfaceName ?? "ergo0"}[/] [dim]({t.DnsMode})[/]");
            var upstreamBadge = t.UpstreamReachable
                ? $"[green]Reachable[/] [dim]({t.LastProbeLatencyMs:F1}ms)[/]"
                : $"[red]Unreachable[/] [dim]({Markup.Escape(t.LastProbeMessage ?? "failed")})[/]";
            grid.AddRow("[bold]Upstream Proxy:[/]", upstreamBadge);

            // Traffic statistics
            var stats = t.Stats;
            var rxStr = FormatBytes(stats.BytesDown);
            var txStr = FormatBytes(stats.BytesUp);
            grid.AddRow("[bold]Flows / Active:[/]", $"[white]{stats.TotalConnections}[/] total, [cyan]{stats.ActiveConnections}[/] active [dim]({stats.FailedConnections} failed)[/]");
            grid.AddRow("[bold]Traffic (RX / TX):[/]", $"[green]↓ {rxStr}[/] / [blue]↑ {txStr}[/]");
            grid.AddRow("[bold]DNS Queries:[/]", $"[white]{stats.DnsQueries}[/] [dim](Fake-IP)[/]");
            if (stats.UdpRejected > 0 || stats.Ipv6Rejected > 0)
            {
                grid.AddRow("[bold]Rejected Traffic:[/]", $"[yellow]{stats.UdpRejected}[/] UDP, [yellow]{stats.Ipv6Rejected}[/] IPv6 [dim](TCP fallback enforced)[/]");
            }
        }
        else
        {
            grid.AddRow("[bold]Tunnel Message:[/]", $"[dim]{Markup.Escape(t.Message)}[/]");
        }

        // Last test result
        if (status.LastTestResult != null)
        {
            var testBadge = status.LastTestResult.IsSuccess
                ? $"[green]Verified ({status.LastTestResult.LatencyMilliseconds:F1}ms)[/]"
                : $"[red]Failed ({status.LastTestResult.Stage} / {status.LastTestResult.ErrorCode})[/]";
            var testTime = status.LastTestedAt?.ToLocalTime().ToString("g") ?? "Recently";
            grid.AddRow("[bold]Last Diagnostics:[/]", $"{testBadge} [dim]at {testTime}[/]");
            if (!status.LastTestResult.IsSuccess)
            {
                grid.AddRow("[bold]Diagnostic Error:[/]", $"[red]{Markup.Escape(status.LastTestResult.Message)}[/]");
            }
        }

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("[bold cyan] Device-Wide Tunnel Dashboard [/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(2, 1, 2, 1)
        };

        AnsiConsole.Write(panel);

        if (status.HasPendingRecovery)
        {
            AnsiConsole.WriteLine();
            var recoveryPanel = new Panel(new Markup($"[bold red]RECOVERY ALERT:[/] {Markup.Escape(status.RecoveryMessage ?? "Pending recovery state detected.")}\n[dim]Run 'ergoproxy recover' or 'ergoproxy disconnect' to restore default network routing.[/]"))
            {
                Border = BoxBorder.Heavy,
                BorderStyle = new Style(Color.Red)
            };
            AnsiConsole.Write(recoveryPanel);
        }
    }

    public static void RenderProtocols(List<ProtocolSupport> protocols)
    {
        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("[bold]Protocol / Traffic[/]");
        table.AddColumn("[bold]Support Level[/]");
        table.AddColumn("[bold]Behavior & Rationale[/]");

        foreach (var p in protocols)
        {
            var levelBadge = p.Level switch
            {
                SupportLevel.Supported => "[bold green]Supported[/]",
                SupportLevel.Partial => "[bold yellow]Partial[/]",
                SupportLevel.Blocked => "[bold red]Blocked (ICMP Reject)[/]",
                _ => "[dim grey]Unsupported[/]"
            };

            table.AddRow($"[white]{p.Name}[/]", levelBadge, $"[dim]{Markup.Escape(p.Note)}[/]");
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader("[bold cyan] Protocol Transparency Matrix (Property 8) [/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
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
            Header = new PanelHeader("[bold cyan] Connectivity Test Report [/]"),
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

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
