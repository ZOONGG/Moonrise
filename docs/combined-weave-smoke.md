# Combined Veyra + BWH smoke

Prepared 2026-10-09 without launching Moonrise or Minecraft. Existing verified bwh-normal-validation binary, normal OfficialLunar, Current Weave 1.3.4. Only original Veyra and user-confirmed baseline BWH enabled; zero user agents. Separate current synthetic adapter and BWH network adapter retained.

Both production package/API inspections passed. Both namespaces: mcp-named. No duplicate class paths, Mixin config names or declared mutual conflicts. Veyra lists 17 Mixins, BWH 10. Both lists include MinecraftMixin, EntityLivingBaseMixin, EntityPlayerMixin and NetworkManagerMixin in different packages: shared target areas still need runtime verification.

Normal maintained-build resolution needed no replacement. Staged exactly two byte-identical JARs. Normal launch planning, synthetic adapter preparation and MNR4 serialization passed together. Offline temp directory removed; offline-mnr4.txt is evidence only. PrepareOnly repeated successfully. Existing binary reused; no rebuild or broad audit.

Run: pwsh -NoProfile -File %WORKSPACE%\app\tools\Start-CombinedWeaveSmoke.ps1

Opens only Moonrise. Click Launch once manually. Verify both mod initializations, applied Mixins, features, BWH networking and normal JVM shutdown. Close Minecraft normally, then exit Moonrise including tray host. Keep collector console open. Original settings restore automatically after Moonrise exits. After interruption close Moonrise and run the same script with -RestoreOnly.

Backups and sanitized JVM evidence: %LOCALAPPDATA%\Moonrise\dev\logs\combined-weave-smoke-*. combined-weave-smoke.pending.txt identifies the directory. Original JARs and existing uncommitted work preserved. BWH is the existing local API migration artifact, not claimed as an untouched upstream release. No combined runtime PASS claimed. No auth, ControlledGenesis, legacy Stormy, lifecycle or UI changes.
