# Synthetic resolver linkage correction — 2026-10-08

Crash bundle `20261008-232142`, Lunar ID `LCLU-WHCFXFPJSBQN` belongs to
attempt `20261008171808478-1`: JVM 25476, parent 20944, start
17:18:41 UTC / 23:18:41 Omsk. The bundle was collected at 23:21;
the meaningful failure occurred at 23:19:10.

Raw evidence is in `%LOCALAPPDATA%/Moonrise/dev/logs/veyra-smoke-20261008-231755-b23032db65f7459b9e433030a6041744`.
stderr line 1 confirms bridge installation. stdout lines 870–873 identify
`NoClassDefFoundError: dev/moonrise/current/SyntheticResolver` in the injected
`moonrise$resolveSynthetic` method. Reflector initialization and the outer
`Failed to boot Minecraft` are consequences. Linkage evidence at 17:19:10.249 UTC
still shows Args$1 registered in mcp-named, with no generated/defined evidence.
The previous Args$1 failure has therefore not been proven resolved in Minecraft.

The injected method is defined with Genesis, whereas the helper URL is attached
to the game loader. A direct helper reference resolves through Genesis's defining
loader, not the receiver. The method now resolves the helper explicitly through
the requesting game loader and invokes its public generation boundary. Invocation
causes are unwrapped; resolver loader/requesting loader are logged. Generation,
ownership, remapping and synthetic definition remain unchanged.

The Java regression harness now denies helper visibility from Genesis's defining
loader, loads the actual resolver through the game loader, and invokes a separate
minimal fixture provider through that same boundary. Real Weave sandbox generation
and descriptor remapping are checked separately. With `-Xverify:all`, actual
Genesis + Weave accessor transformation passes definition, invocation, duplicate,
concurrent, unknown-name and ordinary-delegation checks. Fixtures are not embedded.

35 targeted .NET tests passed. Release build succeeded with zero warnings/errors
in `runtime/current-synthetic-linkage-build/Release/net8.0-windows/win-x64`.
Smoke script CheckOnly and git diff --check passed. The old output directory is
locked by existing Moonrise PID 24948; the script uses the new build directory.
Veyra SHA remains 779760392BCA1F269D0EB325E78950249D355E94A09C90988108C3CCD94D95BD.
No original archives, accounts, ControlledGenesis, legacy implementation or
timeout/lifecycle code changed. No Minecraft launch, publish, merge or commit.

Close old Moonrise and complete its previous collector before running:

```powershell
pwsh -NoProfile -File %WORKSPACE%\app\tools\Start-VeyraSmoke.ps1
```

Click Launch once manually. Require resolver evidence followed by generated and
defined Args$1 evidence in the same JVM; review the first new exception, if any.
Full Minecraft and legacy Stormy runtime behavior remain untested this turn.

READY FOR MANUAL VEYRA SMOKE: YES
