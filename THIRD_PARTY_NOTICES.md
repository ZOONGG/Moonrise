# Third-party notices

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

Lunar Client, Minecraft, Mojang, Microsoft, and Weave are separate projects, products, or trademarks. Their names and identifying marks are used only to describe compatibility with existing Lunar profiles and local Weave packages. Such use does not imply sponsorship, endorsement, ownership, or affiliation.

Moonrise does not redistribute Lunar Client, Minecraft, or the official Lunar Launcher's account/session files. Third-party mod JARs and Java agents are user-supplied executable code and are not part of Moonrise, are not covered by the Moonrise MIT License, and must not be placed in this repository or a Moonrise release archive.
