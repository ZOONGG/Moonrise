# Native bridge deployment Access Denied (2026-10-08)

Attempt `20261008160430954-1` reached `bridge-launch`, after session and
empty mod directory creation, before recording `nativeBridgePath` or starting
Lunar. The historical report only retained the exception message, not a stack.

Target:
`%USERPROFILE%\AppData\Local\Moonrise\dev\cache\runtime\bridge\Moonrise.Native.dll`.

Operation: `NativeBridgeDeploymentService.Ensure` replaces an outdated cache
using `File.Move(temporaryPath, fullPath, overwrite: true)`.
Observed SHA-256: `6C3C39D373B6785122EFBA5A083DD8365E0BD0B19310F13E1E574EC0A7BDFE48`;
expected: `F9C3F2185DDBCE9A58B8D11F2C78769E06AA820F45F6595C0109B80AB84411FC`.
The target was mapped into `powershell`, PID 11372. The file had Archive,
without ReadOnly, and the current user's ACL granted FullControl.

A same-directory copy of the production bridge could be created, but moving
it over this target reproduced:

```text
System.UnauthorizedAccessException: Access to the path is denied.
   at System.IO.FileSystem.MoveFile(String sourceFullPath, String destFullPath, Boolean overwrite)
```

The locked-file regression test against the old application assembly also
reproduced the service stack:

```text
System.UnauthorizedAccessException: Access to the path is denied.
   at System.IO.FileSystem.MoveFile(String sourceFullPath, String destFullPath, Boolean overwrite)
   at Moonrise.Services.NativeBridgeDeploymentService.Ensure(String targetPath)
   at Moonrise.Tests.NativeBridgeDeploymentTests.Ensure_DoesNotReplaceLockedPreviousBridge()
```

Windows refuses replacement of a mapped DLL. Deployment now uses a SHA-256
subdirectory, preserving the locked old DLL and validating the new bytes.
No process was terminated and no permissions were changed.
Launch reports now preserve exception type, HRESULT, stack, and deployment
operation/target/temp paths through the existing redactor.

The dev smoke previously deployed the production bridge before substituting
the startup diagnostic bridge, so the same cache collision blocked it too.
The preparation script now checks its bridge before configuration writes,
uses unique evidence directories, and accepts an explicit executable path.

Validation: Release solution build succeeded in `.work/access-denied-build`
(the standard executable was locked by open Moonrise PID 17648); all three
NativeBridgeDeploymentTests passed against that newly built Moonrise assembly;
PowerShell syntax parsing and `git diff --check` passed.
Lunar/Minecraft were not launched. Existing uncommitted work was preserved.

Close the existing Moonrise window, then from the workspace root run:

```powershell
& .\app\tools\Start-LegacyWeaveLoaderSmoke.ps1 -ExecutablePath %WORKSPACE%\app\.work\access-denied-build\Moonrise.exe
```

This opens Moonrise; click Launch manually once. Runtime success remains
pending that user smoke. Reports are under `%LOCALAPPDATA%\Moonrise\dev\logs`.
