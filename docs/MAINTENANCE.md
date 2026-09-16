# Repository maintenance

## Release checklist

1. Confirm the working tree contains no user JARs, settings, logs, tokens, account files, private paths, or generated binaries other than `runtime/bridge/Moonrise.Native.dll`.
2. Regenerate branding with `tools/generate-brand-assets.ps1`.
3. Capture real English and Russian application screenshots with generic demo data under `docs/images/en/` and `docs/images/ru/`, then restore the local user folders and settings.
4. Regenerate the social preview from `docs/images/en/moonrise-launch.png` with `tools/create-social-preview.ps1`.
5. Delete all previous local release/build directories and archives.
6. Run a clean restore, Release build, all tests, and a clean `win-x64` publish to `release/Moonrise/`.
7. Inspect the staged diff and published archive before creating a release commit or tag.

## GitHub social preview

GitHub does not read `docs/images/moonrise-social-preview.png` automatically. Upload it manually:

1. Open the GitHub repository.
2. Select **Settings → General**.
3. Find **Social preview**.
4. Choose **Edit**, upload `docs/images/moonrise-social-preview.png`, and save.

The file must remain exactly 1280×640 and must not show personal data.

## Repository settings

- Keep the description and topics in [GITHUB_SETUP.md](GITHUB_SETUP.md) current.
- Show Releases on the repository home page; do not show Packages unless Moonrise begins publishing a useful package.
- Protect `main` with pull-request and passing-build requirements.
- Keep Actions permissions at the least privilege required by the workflows.

## Branding

`assets/branding/moonrise-icon.svg` and `moonrise-tray.svg` are the design masters. Raster and ICO exports are deterministic outputs of `tools/IconGenerator`. Review the 16 px full icon and tray icon after any geometry or color change.
