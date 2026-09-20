# Moonrise Theme Packs V1

Moonrise theme packs are appearance data, not plugins. They cannot run code or supply XAML.

## Location

Custom themes live in `%LOCALAPPDATA%\Moonrise\themes`. Each theme has its own folder:

```text
themes/
  my-theme/
    theme.json
    README.txt
    assets/
      background.jpg
```

Development/visual-QA runs use the equivalent `themes` directory under their isolated data root. Built-in themes are compiled into Moonrise and never require files in this directory.

## Built-in themes

- `standard` — **Moonrise Standard**, the restored original Moonrise presentation and the default for new settings.
- `moonlight` — deeper navy surfaces with violet-blue gradients and soft depth.
- `ember` — compact graphite surfaces with warm amber controls.
- `porcelain` — complete light appearance with dark text and strong accessible controls.

## Complete example

```json
{
  "schemaVersion": 1,
  "id": "violet-workshop",
  "name": "Violet Workshop",
  "author": "Example Author",
  "version": "1.0.0",
  "description": "A compact purple theme.",
  "base": "moonlight",
  "colors": {
    "AppBackground": "#100C18",
    "SidebarBackground": "#171020",
    "SurfacePrimary": "#1D1628",
    "SurfaceSecondary": "#251C31",
    "SurfaceRaised": "#30233F",
    "SurfaceHover": "#352747",
    "SurfaceSelected": "#432C62",
    "BorderPrimary": "#533D69",
    "BorderSubtle": "#332640",
    "BorderStrong": "#755591",
    "TextPrimary": "#FFF7FF",
    "TextSecondary": "#D4BFDA",
    "TextMuted": "#A087AA",
    "TextDisabled": "#715E78",
    "AccentPrimary": "#C066FF",
    "AccentSecondary": "#7580FF",
    "AccentSoft": "#39224B",
    "Success": "#4FD09B",
    "Warning": "#F0BD63",
    "Danger": "#F16D80",
    "Info": "#55BFE5",
    "FocusRing": "#C986FF",
    "Selection": "#432C62",
    "ScrollbarTrack": "#21182C",
    "ScrollbarThumb": "#9A73B3"
  },
  "typography": {
    "baseSizeScale": 1.0,
    "headingWeight": "SemiBold",
    "bodyWeight": "Normal",
    "letterSpacing": 0
  },
  "geometry": {
    "cardRadius": 8,
    "buttonRadius": 6,
    "inputRadius": 6,
    "popupRadius": 8,
    "borderThickness": 1,
    "spacingScale": 0.9,
    "cardPadding": 16,
    "controlHeight": 38
  },
  "effects": { "shadowOpacity": 0.2, "surfaceOpacity": 1.0 },
  "variants": {
    "sidebar": "floating",
    "navigation": "left-accent",
    "cards": "bordered",
    "buttons": "gradient",
    "inputs": "outlined",
    "toggle": "compact",
    "scrollbar": "rounded",
    "density": "compact"
  },
  "background": {
    "mode": "gradient",
    "gradientStart": "#271336",
    "gradientEnd": "#100C18",
    "opacity": 1.0,
    "stretch": "UniformToFill",
    "alignment": "Center"
  },
  "assets": {}
}
```

Unknown fields and unknown color-token names are ignored for forward compatibility. Invalid supported values reject the theme with a diagnostic.

## Semantic color tokens

Supported tokens are `AppBackground`, `SidebarBackground`, `SurfacePrimary`, `SurfaceSecondary`, `SurfaceRaised`, `SurfaceHover`, `SurfaceSelected`, `BorderPrimary`, `BorderSubtle`, `BorderStrong`, `TextPrimary`, `TextSecondary`, `TextMuted`, `TextDisabled`, `AccentPrimary`, `AccentSecondary`, `AccentSoft`, `Success`, `Warning`, `Danger`, `Info`, `FocusRing`, `Selection`, `ScrollbarTrack`, and `ScrollbarThumb`.

Colors use `#RRGGBB` or `#AARRGGBB`.

## Geometry and typography

Geometry is clamped to safe bounds: radii 0–28, borders 0–3, spacing 0.75–1.35, card padding 10–32, and control height 34–52. `baseSizeScale` is 0.85–1.25. Weight choices are `Normal`, `Medium`, `SemiBold`, and (for headings) `Bold`. V1 uses installed/bundled UI fonts only; theme font files are not loaded.

## Component variants

- `sidebar`: `flat`, `floating`, `glass`
- `navigation`: `pill`, `left-accent`, `soft-block`
- `cards`: `flat`, `bordered`, `glass`, `elevated`
- `buttons`: `solid`, `gradient`, `soft`, `outline`
- `inputs`: `filled`, `outlined`, `soft`
- `toggle`: `compact`, `soft`, `minimal`
- `scrollbar`: `minimal`, `rounded`, `high-contrast`
- `density`: `compact`, `comfortable`

These names select Moonrise-owned styling behavior. A theme cannot provide a control template.

## Backgrounds and assets

Background modes are `solid`, `gradient`, and `image`. Image mode requires `background.asset`, for example `assets/background.jpg`. Supported image types are PNG, JPG, and JPEG. Individual assets are limited to 20 MB; imported packs are limited to 32 MB and 64 files.

Assets must be relative paths inside the theme folder. Absolute paths, drive/UNC paths, `..` traversal, links/junction escapes, remote URLs, SVG, WEBP, executables, scripts, fonts, DLLs, and XAML are rejected. Recognized asset roles are `appBackground`, `sidebarBackground`, `heroBackground`, and `surfaceTexture`.

## Create, import, and hot reload

`Create theme template` creates a valid duplicate-safe folder (`my-theme`, `my-theme-2`, and so on) and never overwrites existing files. `Import folder` copies a validated theme directory into the theme root without overwriting an installed id. Archive import is intentionally not part of V1.

While a custom theme is selected, Moonrise watches its `theme.json` and supported images. Saves are debounced by 400 ms, parsed and validated off the UI thread, then applied on the UI thread. A malformed update keeps the previous valid appearance and reports a concise diagnostic. Restoring valid JSON hot reloads normally.

## Failure and migration behavior

Invalid JSON, unsupported schemas, unsafe paths, missing assets, and invalid supported values cannot crash startup. The pack is excluded and Moonrise Standard is selected. Persisted legacy values such as `Obsidian` and `Aurora` therefore migrate safely to Moonrise Standard when no matching built-in exists. Only the last successfully applied theme id is persisted.

## Trust model

Theme packs are untrusted input. The loader performs JSON deserialization into fixed data models and maps values to Moonrise-owned WPF resources. It never loads external resource dictionaries, markup extensions, CLR types, C#/DLLs, PowerShell, JavaScript, commands, URLs, or processes, and it makes no network requests.
