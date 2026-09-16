# Moonrise brand guide

The Moonrise mark combines a crescent moon with a curved horizon. The crescent represents discovery and a new launch; the horizon represents a stable boundary between Moonrise and the official clients it coordinates with. The silhouette is original and contains no letters, client marks, game textures, or third-party artwork.

## Palette

| Role | Color | Hex |
| --- | --- | --- |
| Night | Deep indigo | `#0B0D27` |
| Surface | Dark violet | `#2B114E` |
| Primary | Moon violet | `#8D52F5` |
| Highlight | Pale lavender | `#D4C4FF` |
| Accent | Restrained cyan | `#56DDE1` |

## Usage

- Use `moonrise-icon.svg` as the vector master and the exported PNG or ICO matching the target platform.
- Use the full application icon for the executable, windows, taskbar, documentation, social graphics, and large UI surfaces.
- Use the simplified tray icon only for the Windows notification area and similarly constrained 16–32 px contexts.
- Keep clear space around the mark equal to at least one eighth of its displayed width.
- On busy backgrounds, prefer the circular application icon rather than placing the bare crescent directly over imagery.

## Do not

- Do not add initials, words, stars, Minecraft textures, or third-party client marks inside the icon.
- Do not rotate, skew, stretch, outline, recolor, or separate the crescent and horizon.
- Do not use the full icon where the tray icon is required at very small sizes.
- Do not reduce contrast between the crescent, horizon, and night background.

Run `tools/generate-brand-assets.ps1` from the repository root to reproduce every PNG and ICO export from the source-controlled vector geometry.
