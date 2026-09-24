# Moonrise native process bridge

This x64 DLL is injected into the Lunar launcher process created by
Moonrise. It hooks `CreateProcessW` with the official MinHook library,
propagates itself through newly created child processes, and sets
`JAVA_TOOL_OPTIONS` inside a suspended `java.exe`/`javaw.exe` before its main
thread runs.

Before every intercepted `CreateProcessW`, the bridge creates a private copy of
the child environment block and inserts `JAVA_TOOL_OPTIONS` derived from the
launch-specific configuration named by the process-local
`MOONRISE_BRIDGE_CONFIG` environment variable. The caller's block and the
process-wide environment are restored immediately after process creation.

The bridge:

- does not read or alter a Java command line;
- does not inspect authentication files or tokens;
- does not write inside the Lunar Client installation;
- does not change global or user environment variables;
- rejects a missing, empty, relative, invalid, or missing
  `MOONRISE_BRIDGE_CONFIG` path without falling back to a DLL-adjacent file;
- reads launch-specific `MNR3` or structured `MNR4` configuration;
- MNR4 carries the Weave mode, ordered agent paths/options/roles, explicit JVM
  arguments and JVM properties;
- writes PID/error-code-only diagnostics to
  `%LOCALAPPDATA%\Moonrise\logs\native-bridge.log`.

MinHook is used under its BSD 2-Clause license.

The bridge accepts the private `MNR3` wire protocol for compatibility and the
structured `MNR4` LaunchPlan protocol. Both formats contain only local launch
configuration and do not read Lunar authentication state.

## Reproducible build

The bridge is built for Windows x64 with MSVC and MinHook v1.3.4 pinned to commit:

`c3fcafdc10146beb5919319d0683e44e3c30d537`

Run from PowerShell on a machine with Visual Studio 2022 or Build Tools and the C++ x64 workload:

```powershell
./tools/Build-NativeBridge.ps1
```

The script locates `vcvars64.bat`, checks out the exact MinHook commit, compiles the bridge and MinHook sources, verifies the output is an x64 PE image, and prints its SHA-256. CI compiles this verification artifact on every main/PR build.

The verification build does **not** automatically replace `runtime/bridge/Moonrise.Native.dll`. Runtime binary replacement remains an explicit reviewed step because `NativeBridgeDeploymentService.ExpectedSha256` pins the production artifact.
