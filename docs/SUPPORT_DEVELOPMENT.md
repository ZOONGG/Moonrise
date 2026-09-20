# Support UI development

Moonrise Support uses a public API base URL. The desktop app contains no Telegram bot token,
webhook secret, provider credential, or shared API secret. Debug and other development builds
default to `http://127.0.0.1:8787`; Release builds default to
`https://moonrise-backend-production.up.railway.app`. The selected build-time endpoint is embedded
as assembly metadata, so a copied single-file executable keeps working without a companion
configuration file.

For local testing, start the private backend on loopback port `8787`, then launch Moonrise from the
same PowerShell session:

```powershell
$env:MOONRISE_SUPPORT_API_BASE_URL = 'http://127.0.0.1:8787'
dotnet run --project src/Moonrise/Moonrise.csproj
```

To override the embedded default for one machine, place this public configuration beside
`Moonrise.exe`:

```json
{"baseUrl":"http://127.0.0.1:8787"}
```

Name the file `moonrise-support.json`. Runtime resolution is, in descending priority:

1. `MOONRISE_SUPPORT_API_BASE_URL`;
2. the AppContext override and `moonrise-support.json` development override;
3. the build-time `MoonriseSupportApiBaseUrl` value, including the configuration-specific default.

Loopback HTTP is accepted only for development. Non-loopback endpoints must use HTTPS. A publish
can override either configuration default explicitly, for example
`-p:MoonriseSupportApiBaseUrl=https://support.example.com`.

The checkout capability token remains in memory for the active modal only. Closing Support,
changing the amount, or exiting Moonrise cancels polling and releases the token reference.
