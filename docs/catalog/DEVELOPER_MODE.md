# Developer mode

Enable **Settings → Developer mode** to show the **Developers** page. The page remains absent from primary navigation by default.

The inspector can:

- identify a local JAR as a Weave mod or Java agent;
- show `META-INF/MANIFEST.MF` and `weave.mod.json`;
- list declared entry points and mixin configurations;
- identify hook-named classes;
- read the Java class-file version;
- validate the ZIP structure;
- calculate SHA-256;
- create and export a draft package manifest;
- open a prefilled GitHub submission page.

Moonrise does not decompile JARs. Class/package listing is a separate explicit action and returns names only. Drafts default to `sourceOnly`; developers must add an upstream artifact URL and release details before a package becomes installable.

A custom test catalog URL is stored separately from the production URL and is consulted only while developer mode is active. Resetting the catalog cache removes cached metadata, not installed or manually imported packages.
