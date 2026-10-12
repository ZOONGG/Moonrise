# Third-party notices

## Shipped catalog packages and mixed adapter

Moonrise Cosmetics 1.3.2 and Veyra 0.1.1-moonrise.1 are supplied for offline
catalog installation with pinned SHA-256 digests. Veyra derives from
Tryflle/stormy; the owner confirmed redistribution permission on 2026-10-12.
This permission is not a general grant to relicense upstream code as MIT.
Other user-supplied mods are not redistributed.

The mixed adapter includes modified Weave Loader 0.2.6 API classes under
GPL-3.0. Its license, original source snapshot, modification sources and build
script accompany the application in licenses/mixed-weave. See that directory's
README.md. These components retain their own license, separate from Moonrise.

Moonrise source code is licensed under the MIT License. The Moonrise license applies only to the original Moonrise code and assets in this repository. Every third-party dependency or external product retains its own license, copyright, and trademark terms.

## Runtime and source dependencies

| Component | Version used | Purpose | Upstream terms |
| --- | --- | --- | --- |
| .NET / WPF | 8.0 | Windows desktop runtime and UI framework | MIT and related .NET Foundation notices |
| Microsoft.Data.Sqlite | 8.0.11 | Read-only access to the official Lunar Launcher's profile database | MIT |
| SQLite / SQLitePCLRaw transitive packages | Resolved by NuGet lock files | SQLite native and managed integration | SQLite public domain dedication; SQLitePCLRaw Apache-2.0 |
| Weave Loader | 1.3.4 | Runtime Weave Java agent, downloaded on demand | GNU GPL v3 |
| MinHook | 1.3.4 | Native process-creation hook used by the Moonrise launch component | BSD 2-Clause |

Sources:

- <https://github.com/dotnet/runtime>
- <https://github.com/dotnet/efcore/tree/v8.0.11/src/Microsoft.Data.Sqlite>
- <https://www.sqlite.org/copyright.html>
- <https://github.com/ericsink/SQLitePCL.raw>
- <https://github.com/Weave-MC/Weave-Loader/tree/1.3.4>
- <https://github.com/TsudaKageyu/minhook/tree/v1.3.4>

Moonrise downloads the official Weave Loader 1.3.4 release asset at runtime and validates its SHA-256 digest and manifest entry point. The loader is not committed to this repository or included in Moonrise release archives.

MinHook's BSD 2-Clause notice:

> Copyright (C) 2009-2017 Tsuda Kageyu. All rights reserved.
>
> Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
>
> 1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
> 2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE, ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## Development and test dependencies

The test project uses Microsoft.NET.Test.Sdk 17.12.0, xUnit 2.9.2, xunit.runner.visualstudio 2.8.2, and coverlet.collector 6.0.2. These tools are restored from NuGet for development and CI; they are not included in the Moonrise application archive. Their upstream licenses remain in effect.

## External products, code, and trademarks

Moonrise Cosmetics 1.3.2 bundles only the shaded ASM dependency from the pinned
Weave Loader 1.3.4 archive, without Weave bootstrap or loader classes. ASM is
licensed under the [3-Clause BSD License](https://asm.ow2.io/license.html);
the complete notice is included as `ASM-LICENSE.txt` in the agent and in
`extensions/Moonrise.Cosmetics/ASM-LICENSE.txt`.

Lunar Client, Minecraft, Mojang, Microsoft, and Weave are separate projects, products, or trademarks. Their names and identifying marks are used only to describe compatibility with existing Lunar profiles and local Weave packages. Such use does not imply sponsorship, endorsement, ownership, or affiliation.

Moonrise does not redistribute Lunar Client, Minecraft, or the official Lunar Launcher's account/session files. Apart from the expressly permitted catalog packages and adapters described above, third-party mod JARs and Java agents are user-supplied executable code and are not part of Moonrise, are not covered by the Moonrise MIT License, and must not be placed in this repository or a Moonrise release archive.

## Inspiration and source ancestry

Moonrise was inspired by [Nilsen84's Lunar Client QT](https://github.com/Nilsen84/lunar-client-qt), [Lunar Client QT 2](https://github.com/Nilsen84/lcqt2), and the [Youded-byte fork](https://github.com/Youded-byte/lunar-client-qt). Credit belongs to their authors and contributors for the launcher idea. Moonrise has its own application architecture, interface and compatibility mechanisms; inspiration alone does not establish copied code. The original/fork declare GPL-3.0; LCQT2 carries its own LICENSE. Those terms apply to their respective code, not automatically to all Moonrise code.

[Veyra](https://github.com/ZOONGG/Moonrise-Veyra) derives from the archived [Tryflle/stormy](https://github.com/Tryflle/stormy) source. Credit its original authors and contributors for that foundation. Neither repository currently declares a public license. The owner-confirmed permission recorded above is the distribution basis for the reviewed bundled Veyra build; it is not a general open-source license or a grant to relicense Stormy-derived code under Moonrise MIT. Preserve the permission evidence and upstream notices when distributing derived builds.

Mixed-Weave license/source files are under `extensions/MixedWeave/` in source and copied to `licenses/mixed-weave/` during publish. ASM's full notice is copied to `licenses/ASM-LICENSE.txt` and included in the Cosmetics agent. The published directory is not a repository-root path.
