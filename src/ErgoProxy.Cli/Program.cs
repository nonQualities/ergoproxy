using ErgoProxy.Cli.Commands;
using ErgoProxy.Cli.UI;
using ErgoProxy.Core.Credentials;
using ErgoProxy.Core.Network;
using ErgoProxy.Core.Platform;
using ErgoProxy.Core.Services;
using ErgoProxy.Core.Storage;
using ErgoProxy.Core.Validation;

namespace ErgoProxy.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var configDir = Environment.GetEnvironmentVariable("ERGOPROXY_CONFIG_DIR") ??
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "ergoproxy");

        var profileRepo = new JsonProfileRepository(Path.Combine(configDir, "profiles.json"));
        var stateManager = new JsonStateManager(Path.Combine(configDir, "state.json"));
        var credentialStore = CredentialStoreFactory.CreateDefault(configDir);
        var validator = new ProfileValidator();
        var proxyTester = new ProxyTester(validator);
        var platformAdapter = PlatformAdapterFactory.CreateDefault();

        var proxyService = new ProxyService(
            profileRepo,
            stateManager,
            credentialStore,
            proxyTester,
            platformAdapter,
            validator);

        // If no arguments or explicitly requesting interactive mode, launch TUI
        if (args.Length == 0 || args[0].Equals("interactive", StringComparison.OrdinalIgnoreCase) || args[0].Equals("tui", StringComparison.OrdinalIgnoreCase))
        {
            var menu = new InteractiveMenu(proxyService);
            await menu.RunAsync();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var cmdArgs = args.Skip(1).ToArray();

        return command switch
        {
            "configure" => await ConfigureCommand.ExecuteAsync(proxyService, cmdArgs),
            "profiles" or "list" => await ProfilesCommand.ExecuteAsync(proxyService, cmdArgs),
            "test" => await TestCommand.ExecuteAsync(proxyService, cmdArgs),
            "connect" or "enable" => await ConnectCommand.ExecuteAsync(proxyService, cmdArgs),
            "disconnect" or "disable" => await DisconnectCommand.ExecuteAsync(proxyService, cmdArgs),
            "status" => await StatusCommand.ExecuteAsync(proxyService, cmdArgs),
            "help" or "--help" or "-h" => ShowHelpAndReturnZero(),
            _ => ShowUnknownCommand(command)
        };
    }

    private static int ShowHelpAndReturnZero()
    {
        HelpCommand.Execute();
        return 0;
    }

    private static int ShowUnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: '{cmd}'. Run 'ergoproxy help' for available commands.");
        return 1;
    }
}
