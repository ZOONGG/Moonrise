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
- reads only the launch-specific `MNR3` configuration containing the Weave
  agent, enabled-mod directory, and enabled Java-agent paths;
- writes PID/error-code-only diagnostics to
  `%LOCALAPPDATA%\Moonrise\logs\native-bridge.log`.

MinHook is used under its BSD 2-Clause license.

The bridge uses the private `MNR3` wire protocol between the managed launcher
and the injected process hook. It contains only local launch paths and has no
legacy fallback.
