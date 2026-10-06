using Spectre.Console;

namespace ErgoProxy.Cli.UI;

public static class Theme
{
    public static readonly Color Primary = Color.Cyan1;
    public static readonly Color Success = Color.Green;
    public static readonly Color Warning = Color.Yellow;
    public static readonly Color Danger = Color.Red;
    public static readonly Color Muted = Color.Grey;

    public static void RenderHeader()
    {
        AnsiConsole.Write(
            new FigletText("ERGOPROXY")
                .Centered()
                .Color(Primary));

        AnsiConsole.Write(
            new Markup("[bold grey]Cross-Platform HTTP Proxy Manager[/] [dim]v1.0 (Linux | Windows | macOS)[/]\n\n")
                .Centered());
    }

    public static void ShowSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[bold green]✔[/] [green]{Markup.Escape(message)}[/]");
    }

    public static void ShowWarning(string message)
    {
        AnsiConsole.MarkupLine($"[bold yellow]▲[/] [yellow]{Markup.Escape(message)}[/]");
    }

    public static void ShowError(string message)
    {
        AnsiConsole.MarkupLine($"[bold red]✖[/] [red]{Markup.Escape(message)}[/]");
    }

    public static void ShowInfo(string message)
    {
        AnsiConsole.MarkupLine($"[bold cyan]ℹ[/] [cyan]{Markup.Escape(message)}[/]");
    }
}
