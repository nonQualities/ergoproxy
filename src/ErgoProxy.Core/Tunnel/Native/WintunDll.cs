using System.Reflection;
using System.Runtime.InteropServices;

namespace ErgoProxy.Core.Tunnel.Native;

/// <summary>
/// Native bindings for WireGuard's Wintun layer-3 ring-buffer TUN driver (wintun.dll).
/// </summary>
internal static unsafe partial class WintunDll
{
    public const uint WINTUN_MIN_RING_CAPACITY = 0x20000;      // 128 KiB
    public const uint WINTUN_MAX_RING_CAPACITY = 0x4000000;    // 64 MiB
    public const uint WINTUN_DEFAULT_RING_CAPACITY = 0x400000; // 4 MiB

    static WintunDll()
    {
        if (OperatingSystem.IsWindows())
        {
            NativeLibrary.SetDllImportResolver(typeof(WintunDll).Assembly, ResolveWintunDll);
        }
    }

    private static nint ResolveWintunDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "wintun.dll" && libraryName != "wintun")
            return 0;

        // 1. Try AppContext.BaseDirectory
        var baseDir = AppContext.BaseDirectory;
        var directPath = Path.Combine(baseDir, "wintun.dll");
        if (File.Exists(directPath) && NativeLibrary.TryLoad(directPath, out var handle))
            return handle;

        // 2. Try runtimes subfolder based on architecture
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            Architecture.X86 => "win-x86",
            _ => "win-x64"
        };
        var runtimesPath = Path.Combine(baseDir, "runtimes", arch, "native", "wintun.dll");
        if (File.Exists(runtimesPath) && NativeLibrary.TryLoad(runtimesPath, out handle))
            return handle;

        // 3. Try ProgramData\ergoproxy\bin\wintun.dll
        var progData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ergoproxy", "bin", "wintun.dll");
        if (File.Exists(progData) && NativeLibrary.TryLoad(progData, out handle))
            return handle;

        // 4. Default OS loader search
        if (NativeLibrary.TryLoad("wintun.dll", assembly, searchPath, out handle))
            return handle;

        return 0;
    }

    [LibraryImport("wintun.dll", EntryPoint = "WintunCreateAdapter", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint WintunCreateAdapter(string name, string tunnelType, in Guid requestedGuid);

    [LibraryImport("wintun.dll", EntryPoint = "WintunOpenAdapter", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint WintunOpenAdapter(string name);

    [LibraryImport("wintun.dll", EntryPoint = "WintunCloseAdapter")]
    public static partial void WintunCloseAdapter(nint adapter);

    [LibraryImport("wintun.dll", EntryPoint = "WintunDeleteDriver")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WintunDeleteDriver();

    [LibraryImport("wintun.dll", EntryPoint = "WintunStartSession", SetLastError = true)]
    public static partial nint WintunStartSession(nint adapter, uint capacity);

    [LibraryImport("wintun.dll", EntryPoint = "WintunEndSession")]
    public static partial void WintunEndSession(nint session);

    [LibraryImport("wintun.dll", EntryPoint = "WintunGetReadWaitEvent")]
    public static partial nint WintunGetReadWaitEvent(nint session);

    [LibraryImport("wintun.dll", EntryPoint = "WintunReceivePacket")]
    public static partial byte* WintunReceivePacket(nint session, out uint packetSize);

    [LibraryImport("wintun.dll", EntryPoint = "WintunReleaseReceivePacket")]
    public static partial void WintunReleaseReceivePacket(nint session, byte* packet);

    [LibraryImport("wintun.dll", EntryPoint = "WintunAllocateSendPacket")]
    public static partial byte* WintunAllocateSendPacket(nint session, uint packetSize);

    [LibraryImport("wintun.dll", EntryPoint = "WintunSendPacket")]
    public static partial void WintunSendPacket(nint session, byte* packet);
}
