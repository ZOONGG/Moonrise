# Dev-only Mixin service discovery trace

Build Moonrise into `.work/mixin-service-diagnostic/build`, then run `tools/Start-MixinServiceDiagnostic.ps1` after closing Moonrise and JVMs and disabling all packages. It builds a separate diagnostic agent, starts the dev smoke executable, and prints the evidence directory. Press Launch once in that Moonrise window.

The script changes no package selections or launcher settings. Two `MOONRISE_MIXIN_DIAGNOSTIC_*` variables are inherited by the dev smoke process and removed from the calling PowerShell environment immediately after spawn. Only development mode with `--dev-legacy-weave-smoke` reads them, adding the trace agent to the existing structured MNR4 plan before Weave. Normal launch selection is unchanged. The earlier attempt using `JAVA_TOOL_OPTIONS` produced no diagnostic premain marker in OfficialLunar and is not used.

The diagnostic agent uses ASM from the untouched Weave JAR in an isolated loader with the platform loader as parent. It does not append ASM or Mixin to the game/system classpath. It logs relevant definitions, defining-loader chains, code sources, and actual service resources. In the in-memory `MixinService.initService` only, it inserts `Throwable.printStackTrace` into the existing `ServiceConfigurationError` handler. The original handler, service iteration, provider constructors, selection, and validation remain intact. No on-disk third-party class or JAR is modified.

The trace contains service metadata and exception stacks only; it does not inspect command lines, authentication, account files, or arbitrary runtime properties. Output lives under `%LOCALAPPDATA%/Moonrise/dev/logs/mixin-service-diagnostic-*`. Generated classes and the diagnostic JAR live under ignored `.work/mixin-service-diagnostic`.

The catch instrumentation was verified against the actual Genesis Mixin bytecode on Lunar's Java 17.0.18 in an offline URLClassLoader probe. That probe is a diagnostic validation only: its flat classpath is not evidence for actual Genesis/Ichor behavior. Runtime conclusions require the OfficialLunar trace.
