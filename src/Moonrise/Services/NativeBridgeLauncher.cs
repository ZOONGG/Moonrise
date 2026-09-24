using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class NativeBridgeLauncher
{
    public NativeBridgeLaunchResult Launch(
        string lunarExecutable,
        LaunchPlan launchPlan,
        string enabledModsDirectory,
        string nativeBridgePath,
        string bridgeConfigPath,
        string? launcherArgument = null,
        bool hideLauncherWindow = false)
    {
        ArgumentNullException.ThrowIfNull(launchPlan);
        var projection = Mnr3BridgeProjectionBuilder.Build(launchPlan);
        return Launch(
            lunarExecutable,
            projection.PrimaryAgentPath,
            enabledModsDirectory,
            nativeBridgePath,
            bridgeConfigPath,
            launcherArgument,
            hideLauncherWindow,
            projection.AdditionalAgentPaths);
    }

    public const string BridgeConfigEnvironmentVariable = "MOONRISE_BRIDGE_CONFIG";
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint MemCommit = 0x00001000;
    private const uint MemReserve = 0x00002000;
    private const uint MemRelease = 0x00008000;
    private const uint PageReadWrite = 0x04;
    private const uint WaitObject0 = 0x00000000;
    private const uint StartfUseShowWindow = 0x00000001;
    private const ushort SwHide = 0;

    public NativeBridgeLaunchResult Launch(
        string lunarExecutable,
        string agentPath,
        string enabledModsDirectory,
        string nativeBridgePath,
        string bridgeConfigPath,
        string? launcherArgument = null,
        bool hideLauncherWindow = false,
        IReadOnlyList<string>? additionalAgentPaths = null)
    {
        ValidatePath(lunarExecutable, nameof(lunarExecutable));
        ValidatePath(agentPath, nameof(agentPath));
        ValidatePath(enabledModsDirectory, nameof(enabledModsDirectory));
        ValidatePath(nativeBridgePath, nameof(nativeBridgePath));
        ValidatePath(bridgeConfigPath, nameof(bridgeConfigPath));
        ValidateArgument(launcherArgument, nameof(launcherArgument));
        foreach (var additionalAgentPath in additionalAgentPaths ?? [])
        {
            ValidatePath(additionalAgentPath, nameof(additionalAgentPaths));
            if (!File.Exists(additionalAgentPath))
            {
                throw new FileNotFoundException("A selected Java agent was not found.", additionalAgentPath);
            }
        }

        if (!File.Exists(lunarExecutable))
        {
            throw new FileNotFoundException("Lunar Client executable was not found.", lunarExecutable);
        }
        if (!File.Exists(agentPath))
        {
            throw new FileNotFoundException("Weave agent was not found.", agentPath);
        }
        if (!Directory.Exists(enabledModsDirectory))
        {
            throw new DirectoryNotFoundException(enabledModsDirectory);
        }
        if (!File.Exists(nativeBridgePath))
        {
            throw new FileNotFoundException("Native process bridge was not found.", nativeBridgePath);
        }

        WriteBridgeConfiguration(
            bridgeConfigPath,
            agentPath,
            enabledModsDirectory,
            additionalAgentPaths);

        return LaunchWithInjectedBridge(
            lunarExecutable,
            nativeBridgePath,
            Path.GetFullPath(bridgeConfigPath),
            launcherArgument,
            hideLauncherWindow);
    }

    private static void WriteBridgeConfiguration(
        string bridgeConfigPath,
        string primaryAgentPath,
        string compatibilityDirectory,
        IReadOnlyList<string>? additionalAgentPaths)
    {
        BridgeConfigurationFile.WriteAtomic(
            bridgeConfigPath,
            primaryAgentPath,
            compatibilityDirectory,
            additionalAgentPaths);
    }

    private static NativeBridgeLaunchResult LaunchWithInjectedBridge(
        string launcherExecutable,
        string nativeBridgePath,
        string bridgeConfigPath,
        string? launcherArgument,
        bool hideLauncherWindow)
    {
        var environment = BuildEnvironmentBlock(bridgeConfigPath);
        var environmentPointer = Marshal.StringToHGlobalUni(environment);
        var commandLine = new StringBuilder(JavaToolOptionsBuilder.Quote(Path.GetFullPath(launcherExecutable)));
        if (!string.IsNullOrWhiteSpace(launcherArgument))
        {
            commandLine.Append(' ');
            commandLine.Append(JavaToolOptionsBuilder.Quote(launcherArgument));
        }
        var startupInfo = new StartupInfo
        {
            Size = Marshal.SizeOf<StartupInfo>(),
            Flags = hideLauncherWindow ? StartfUseShowWindow : 0,
            ShowWindow = hideLauncherWindow ? SwHide : (ushort)1
        };
        ProcessInformation processInformation = default;
        var processCreated = false;
        var resumed = false;

        try
        {
            processCreated = CreateProcess(
                Path.GetFullPath(launcherExecutable),
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: false,
                CreateSuspended | CreateUnicodeEnvironment,
                environmentPointer,
                Path.GetDirectoryName(Path.GetFullPath(launcherExecutable)),
                ref startupInfo,
                out processInformation);
            if (!processCreated)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create the launcher process.");
            }

            InjectAndWaitForReady(
                processInformation.ProcessHandle,
                checked((int)processInformation.ProcessId),
                Path.GetFullPath(nativeBridgePath));

            var resumeResult = ResumeThread(processInformation.ThreadHandle);
            if (resumeResult == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to resume Lunar Client process.");
            }
            resumed = true;

            var process = Process.GetProcessById(checked((int)processInformation.ProcessId));
            return new NativeBridgeLaunchResult(process, BridgeReady: true);
        }
        catch
        {
            if (processCreated && !resumed && processInformation.ProcessHandle != IntPtr.Zero)
            {
                _ = TerminateProcess(processInformation.ProcessHandle, 1);
            }
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(environmentPointer);
            if (processInformation.ThreadHandle != IntPtr.Zero)
            {
                CloseHandle(processInformation.ThreadHandle);
            }
            if (processInformation.ProcessHandle != IntPtr.Zero)
            {
                CloseHandle(processInformation.ProcessHandle);
            }
        }
    }

    private static void InjectAndWaitForReady(IntPtr processHandle, int processId, string nativeBridgePath)
    {
        var eventName = $"Local\\MoonriseBridgeReadyV1-{processId}";
        using var ready = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            eventName,
            out _);

        var pathBytes = Encoding.Unicode.GetBytes(nativeBridgePath + '\0');
        var remotePath = VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            checked((nuint)pathBytes.Length),
            MemReserve | MemCommit,
            PageReadWrite);
        if (remotePath == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to allocate bridge path in Lunar process.");
        }

        IntPtr remoteThread = IntPtr.Zero;
        try
        {
            if (!WriteProcessMemory(
                    processHandle,
                    remotePath,
                    pathBytes,
                    checked((nuint)pathBytes.Length),
                    out var bytesWritten) ||
                bytesWritten != checked((nuint)pathBytes.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to write bridge path to Lunar process.");
            }

            var kernel32 = GetModuleHandle("kernel32.dll");
            var loadLibrary = kernel32 == IntPtr.Zero
                ? IntPtr.Zero
                : GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to resolve LoadLibraryW.");
            }

            remoteThread = CreateRemoteThread(
                processHandle,
                IntPtr.Zero,
                0,
                loadLibrary,
                remotePath,
                0,
                out _);
            if (remoteThread == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to start bridge loader in Lunar process.");
            }

            if (WaitForSingleObject(remoteThread, 15_000) != WaitObject0)
            {
                throw new TimeoutException("Timed out while loading the native Moonrise bridge.");
            }
            if (!GetExitCodeThread(remoteThread, out var loadResult) || loadResult == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not load the native Moonrise bridge.");
            }
            if (!ready.WaitOne(TimeSpan.FromSeconds(15)))
            {
                throw new TimeoutException("The native bridge loaded but did not report a ready process hook.");
            }
        }
        finally
        {
            if (remoteThread != IntPtr.Zero)
            {
                CloseHandle(remoteThread);
            }
            _ = VirtualFreeEx(processHandle, remotePath, 0, MemRelease);
        }
    }

    internal static string BuildEnvironmentBlock(string bridgeConfigPath)
    {
        ValidatePath(bridgeConfigPath, nameof(bridgeConfigPath));
        var fullConfigPath = Path.GetFullPath(bridgeConfigPath);
        if (!File.Exists(fullConfigPath))
            throw new FileNotFoundException("The launch bridge configuration was not found.", fullConfigPath);

        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value && key.Length > 0)
            {
                variables[key] = value;
            }
        }

        LunarLaunchEnvironment.Sanitize(variables);
        variables[BridgeConfigEnvironmentVariable] = fullConfigPath;
        variables["MOONRISE_ACTIVE"] = "1";
        return string.Join('\0', variables.Select(static pair => $"{pair.Key}={pair.Value}")) + "\0\0";
    }

    private static void ValidatePath(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.IndexOfAny(['\0', '\r', '\n', '"']) >= 0)
        {
            throw new ArgumentException("The path contains unsafe characters.", parameterName);
        }
    }

    private static void ValidateArgument(string? value, string parameterName)
    {
        if (value is null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['\0', '\r', '\n', '"']) >= 0)
        {
            throw new ArgumentException("The launcher argument contains unsafe characters.", parameterName);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Count;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateProcessW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr process,
        IntPtr address,
        nuint size,
        uint allocationType,
        uint protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        byte[] buffer,
        nuint size,
        out nuint bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(
        IntPtr process,
        IntPtr threadAttributes,
        nuint stackSize,
        IntPtr startAddress,
        IntPtr parameter,
        uint creationFlags,
        out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string functionName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
