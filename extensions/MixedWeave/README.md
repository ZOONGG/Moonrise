# Reviewed mixed runtime artifacts

The two runtime/mixed-weave-adapter JARs are the exact QA-tested 0.1.0
artifacts. They are separate adapters; no original Raven/BWH JAR is shipped
or modified. Hashes are pinned by the repository and release safety scripts.

The ABI JAR contains modified Weave Loader 0.2.6 event/command API classes,
licensed under GPL-3.0. GPL-3.0.txt and the complete upstream source snapshot
Weave-Loader-0.2.6-source.zip accompany the published application. Upstream:
https://github.com/Weave-MC/Weave-Loader/tree/v0.2.6
Source archive SHA-256:
FE9EFB97F292DEB931923094F755D1EF59E8C6696B246CED0A32497648D1F711

Moonrise modifications: Event superclass bridges to current Weave Event;
Kotlin names are relocated to the current loader namespace; EventBus and
ModInitializer route to the current API. Corresponding modification/build
sources are shipped under moonrise-source/ and Build-MixedWeaveAdapter.ps1.
The mixed adapters and these modifications are distributed under GPL-3.0.
No warranty. See the full license for redistribution and modification terms.

The build script documents exact input hashes and javac/jar commands;
rebuilding requires official loader inputs and the user's unchanged Raven
artifact to obtain its metadata. Normal .NET builds use the reviewed ready
artifacts and require neither private JARs nor a JDK.
