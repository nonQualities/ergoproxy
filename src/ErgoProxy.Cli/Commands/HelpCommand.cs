using Spectre.Console;

namespace ErgoProxy.Cli.Commands;

public static class HelpCommand
{
    public static void Execute()
    {
        AnsiConsole.MarkupLine("[bold cyan]ERGOPROXY: Cross-Platform HTTP Proxy Manager[/] [dim](v1.0)[/]\n");
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
        Console.WriteLine("  test         Test connectivity to proxy endpoint");
        Console.WriteLine("               Options: [--id <id>] [--endpoint <url>] [--timeout <sec>] [--json]");
        Console.WriteLine("  connect      Activate selected profile as system proxy");
        Console.WriteLine("               Options: [--id <id>] [--force]");
        Console.WriteLine("  disconnect   Deactivate system proxy and restore previous settings");
        Console.WriteLine("               Options: [--force]");
        Console.WriteLine("  status       Show current proxy configuration and connection status");
        Console.WriteLine("               Options: [--json]");
        Console.WriteLine("  help         Display this help information\n");

        Console.WriteLine("EXIT CODES:");
        Console.WriteLine("  0   Success");
        Console.WriteLine("  1   Configuration / validation error");
        Console.WriteLine("  2   Network / connectivity test failure");
        Console.WriteLine("  3   Platform conflict or unsupported operation\n");
    }
}
