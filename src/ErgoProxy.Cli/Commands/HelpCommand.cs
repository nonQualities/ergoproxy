using Spectre.Console;

namespace ErgoProxy.Cli.Commands;

public static class HelpCommand
{
    public static void Execute()
    {
        AnsiConsole.MarkupLine("[bold cyan]ERGOPROXY: Device-Wide HTTP Proxy Tunnel[/] [dim](v2.0)[/]\n");
        Console.WriteLine("USAGE:");
        Console.WriteLine("  ergoproxy                  Launch interactive terminal interface (TUI)");
        Console.WriteLine("  ergoproxy <command> [opts] Run non-interactive CLI command\n");

        Console.WriteLine("COMMANDS:");
        Console.WriteLine("  configure    Create or update a proxy profile");
        Console.WriteLine("               Options: --name <name> --host <host> --port <port>");
        Console.WriteLine("                        [--auth] [--username <user>] [--password <pass>]");
        Console.WriteLine("                        [--bypass <rules>] [--id <id>]");
        Console.WriteLine("  profiles     List all configured proxy profiles");
        Console.WriteLine("               Options: [--json]");
        Console.WriteLine("  test         Test connectivity and HTTP CONNECT to proxy endpoint");
        Console.WriteLine("               Options: [--id <id>] [--endpoint <url>] [--timeout <sec>] [--json]");
        Console.WriteLine("  connect      Establish device-wide tunnel through proxy");
        Console.WriteLine("               Options: [<profile_name_or_id>] [--id <id>]");
        Console.WriteLine("  disconnect   Deactivate tunnel and restore normal system networking");
        Console.WriteLine("  status       Show current tunnel state, traffic statistics, and protocols");
        Console.WriteLine("               Options: [--json]");
        Console.WriteLine("  recover      Clean up stale TUN interfaces, routing rules, or DNS");
        Console.WriteLine("  daemon       Run background tunnel controller daemon (root)");
        Console.WriteLine("               Options: [--socket <path>] [--uid <uid>] [--detach]");
        Console.WriteLine("  help         Display this help information\n");

        Console.WriteLine("EXIT CODES:");
        Console.WriteLine("  0   Success");
        Console.WriteLine("  1   Configuration, privilege, or tunnel establishment error");
        Console.WriteLine("  2   Network or proxy test failure\n");
    }
}
