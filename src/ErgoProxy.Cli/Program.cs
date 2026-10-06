using ErgoProxy.Cli.Commands;
using ErgoProxy.Cli.UI;
using ErgoProxy.Core.Credentials;
using ErgoProxy.Core.Daemon;
using ErgoProxy.Core.Network;
using ErgoProxy.Core.Services;
using ErgoProxy.Core.Storage;
using ErgoProxy.Core.Tunnel;
using ErgoProxy.Core.Tunnel.Native;
using ErgoProxy.Core.Tunnel.Platform;
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
        var daemonClient = new DaemonClient();

        var proxyService = new ProxyService(
            profileRepo,
            stateManager,
            credentialStore,
            proxyTester,
            daemonClient,
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
            "daemon" => await RunDaemonAsync(cmdArgs),
            "recover" or "reset" => await RunRecoverAsync(stateManager),
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

    private static async Task<int> RunDaemonAsync(string[] args)
    {
        string? socketPath = null;
        uint? targetUid = null;
        var detach = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--socket" && i + 1 < args.Length) socketPath = args[++i];
            else if (a == "--uid" && i + 1 < args.Length && uint.TryParse(args[++i], out var u)) targetUid = u;
            else if (a == "--detach") detach = true;
        }

        if (detach && OperatingSystem.IsLinux())
        {
            LinuxNative.SetSid();
            LinuxNative.DetachStdio();
        }

        var controller = new TunnelController();
        await using var server = new DaemonServer(controller, socketPath, targetUid);
        await server.StartAsync();
        await server.WaitForShutdownAsync();
        return 0;
    }

    private static async Task<int> RunRecoverAsync(IStateManager stateManager)
    {
        Console.WriteLine("Cleaning stale routing rules, TUN interfaces, and DNS configuration...");
        var platform = new LinuxTunnelPlatform();
        await platform.CleanStaleStateAsync(new TunnelAddressing());

        var state = await stateManager.GetStateAsync();
        state.HasPendingRecovery = false;
        state.RecoveryMessage = null;
        await stateManager.SaveStateAsync(state);

        Console.WriteLine("Recovery complete. Sane networking state restored.");
        return 0;
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
