# Lightroom Presets - Logitech MX Creative Console Plugin

Assign individual Adobe **Lightroom Desktop/CC** develop presets to
individual MX Creative Console keys, and apply them to the currently
selected photo with a single press. This is the MX Creative Console
counterpart to a companion Stream Deck plugin built the same way, for the
same purpose - see docs/PROTOCOL.md and docs/ARCHITECTURE.md for full
technical detail.

This targets **Lightroom Desktop/CC only**, via Lightroom's own local
**External Controller API**, and the current **Logi Actions SDK**
(C#/.NET track) - not Lightroom Classic, not accessibility automation, not
UI scripting.

> **Read this before building.** This project was written in a Linux
> sandbox with no access to macOS, Logi Options+, an MX Creative Console,
> or a licensed Lightroom install. Unlike a typical "download and
> double-click" plugin, **there is no prebuilt package in this repo** -
> the C#/.NET project could not be compiled here because it depends on
> `PluginApi.dll`, which only exists inside an installed copy of the Logi
> Plugin Service app (not on NuGet, no standalone download). You will need
> to build it yourself on your Mac - double-clicking
> **`Build and Install.command`** does this for you (see "Build" below), so
> it's a one-click step, not a coding task, but it is a step this repo
> can't skip the way the Stream Deck version could. What *could* be
> verified without a Mac was verified thoroughly - see
> docs/ARCHITECTURE.md's "Build verification" section for exactly what
> that means before you start.

## Requirements

- macOS with **Logi Options+** installed and opened at least once (this
  installs the Logi Plugin Service, which the build depends on).
- **.NET 8 SDK** (`dotnet --version` should report 8.x or a compatible
  later SDK that can still target `net8.0`).
- A Logitech **MX Creative Console** (Keypad and/or Dialpad).
- Adobe Lightroom Desktop/CC (the cloud-based app - **not** Lightroom
  Classic), with **Preferences > Interface > "Enable external
  controllers"** turned on.

## Build

**Easiest way:** download this repo (GitHub's green **Code** button →
**Download ZIP**, then unzip it), make sure you have the **.NET 8 SDK**
installed (see "Requirements" above - if not, `Build and Install.command`
will tell you exactly what to install), then double-click
**`Build and Install.command`** in the unzipped folder. It runs the build
for you and prints plain-English success/failure messages. macOS may warn
that it's from an unidentified developer the first time - right-click it
and choose **Open** to confirm you trust it.

If you'd rather use the terminal directly (or the `.command` file fails
for some reason):

```bash
git clone <this repo>
cd Lightroom-Plugin---MX-Creative-Console-Preset-Shortcut
dotnet build
```

Either way, the build also copies `package/metadata/*` next to the built DLL and
(via the `PostBuild` target) writes a `.link` file that tells the Logi
Plugin Service where to find the plugin - it should appear under "All
Actions" in Options+'s device configuration screen without a separate
install step. If the build fails, check docs/TROUBLESHOOTING.md first -
the most likely cause is a `PluginApi` signature this project's
reconstruction got slightly wrong (see docs/ARCHITECTURE.md).

To build a distributable package once it compiles cleanly on your Mac,
use whichever packaging command your installed SDK/toolkit version
provides (check the Logi Actions SDK docs for the current one - this
produces a `.lplug4` file, matching Logitech's own DemoPlugin's `npm run
build:pack`-equivalent workflow for the Node.js track).

## Using it

1. Enable external controllers in Lightroom (Preferences > Interface),
   restart Lightroom.
2. In Options+, assign **Apply Lightroom Preset** to a key.
3. In its configuration, pick a preset from the dropdown (populated live
   from Lightroom once connected).
4. Optionally assign **Refresh Lightroom Presets** to another key to
   re-sync the list after adding/renaming presets in Lightroom.
5. Press the configured key - Lightroom applies that preset to the
   selected photo.

## Known limitations (see docs/ARCHITECTURE.md for full detail)

- **No preset folders**: Lightroom's API returns a flat preset list, no
  folder/group metadata - same limitation as the Stream Deck version, not
  specific to this SDK.
- **No custom title / live status text on the key face** and **no
  favorites UI** in this version - scoped out because the exact API for
  them couldn't be verified without a real `PluginApi.dll` to inspect (see
  docs/ARCHITECTURE.md's "platform differences" section for specifics and
  how to add them back).
- **No cross-button "stale preset" sweep** - if a preset is deleted,
  buttons using it will fail on next press with "Preset not found", but
  nothing proactively flags every affected button the way the Stream Deck
  version's "Refresh" action does.

## Documentation

- **docs/ARCHITECTURE.md** - how the plugin is put together, why, and
  exactly what could/couldn't be verified while building it.
- **docs/PROTOCOL.md** - what's known about Lightroom's External
  Controller API, with sources.
- **docs/TROUBLESHOOTING.md** - build errors and on-device error states.
- **docs/TESTING.md** - the full test plan, including the critical
  acceptance test to run first.
- **examples/preset-config.example.json** - shape of this plugin's on-disk
  preset cache.

## Project layout

```
LightroomPresetsPlugin/
  LightroomPresetsPlugin.csproj
  LightroomPresetsPlugin.cs         composition root (Plugin subclass)
  LightroomApplication.cs           ClientApplication stub (see ARCHITECTURE.md)
  PluginLog.cs, PluginResources.cs  standard SDK boilerplate
  Lightroom/
    LightroomConnection.cs           WebSocket client for Lightroom's External Controller API
    PresetManager.cs                  preset discovery + on-disk cache
    PresetApplicationService.cs       validates + applies a preset, typed errors
    Models.cs, PluginPaths.cs
  Actions/
    ApplyPresetCommand.cs              "Apply Lightroom Preset" (ActionEditorCommand)
    RefreshPresetsCommand.cs           "Refresh Lightroom Presets" (PluginDynamicCommand)
  package/metadata/LoupedeckPackage.yaml
  images/                             embedded action feedback icons
scripts/
  png.mjs, generate-icons.mjs         dependency-free icon generator (same as the Stream Deck repo)
docs/
examples/
```

## License

This project's own code is MIT-licensed - see LICENSE, which also has the
full attribution/provenance notes for the SDK patterns and protocol
research this was built from.
