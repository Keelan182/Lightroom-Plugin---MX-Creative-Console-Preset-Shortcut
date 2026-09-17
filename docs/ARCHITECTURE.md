# Architecture

```
Logi Options+ (Logi Plugin Service)
   │  Loupedeck/Logi PluginApi (loaded from an installed Logi Plugin Service app)
   ▼
This plugin - a .NET assembly loaded by the Logi Plugin Service
   │
   ├── LightroomConnection            (LightroomPresetsPlugin/Lightroom/LightroomConnection.cs)
   │      - Owns the one persistent WebSocket to Lightroom's External
   │        Controller API (ws://127.0.0.1:7682, or a discovered port).
   │      - register handshake + pairing-GUID persistence.
   │      - request/response correlation by requestId (ConcurrentDictionary
   │        of TaskCompletionSource, supports concurrent in-flight requests).
   │      - liveness polling (macOS `pgrep`), reconnect with exponential
   │        backoff, and a 5-state status: NotRunning / Connecting /
   │        Connected / Disconnected / Unresponsive.
   │      - Exposes a StatusChanged event; nothing above this layer talks
   │        sockets.
   │
   ├── PresetManager                  (LightroomPresetsPlugin/Lightroom/PresetManager.cs)
   │      - RefreshAsync(): getPresetIDs -> getPresetName (bounded
   │        concurrency) -> sorted List<LightroomPreset> -> cached to disk.
   │      - GetPresets()/FindById()/Search() read the in-memory cache.
   │      - Loads its disk cache on startup so buttons can show real preset
   │        names immediately, before Lightroom is even reachable.
   │      - No folder/hierarchy modeling - Lightroom's API doesn't expose
   │        one (see docs/PROTOCOL.md).
   │
   ├── PresetApplicationService       (LightroomPresetsPlugin/Lightroom/PresetApplicationService.cs)
   │      - ApplyAsync(presetId): validates the preset id is configured and
   │        known, checks LightroomConnection.Status, calls "applyPreset",
   │        and translates every failure into a typed
   │        PresetApplicationException the action layer can render without
   │        knowing anything about sockets or JSON.
   │
   └── Actions                        (LightroomPresetsPlugin/Actions/)
          ├── ApplyPresetCommand   - ActionEditorCommand with an
          │     ActionEditorListbox preset dropdown, per-button (each
          │     assigned control instance keeps its own selected preset id -
          │     this is simply how the ActionEditor system already works,
          │     not something this project had to build).
          └── RefreshPresetsCommand - a plain PluginDynamicCommand with a
                real success/error icon swap (GetCommandImage +
                ActionImageChanged), confirmed against Logitech's own
                current DemoPlugin sample.
```

`LightroomPresetsPlugin.cs` is the composition root: it constructs one
`LightroomConnection`, one `PresetManager`, and one
`PresetApplicationService` in `Load()`, exposes them as static properties so
both actions can reach them, and stops the connection in `Unload()`.

## Why this split

Identical reasoning to the Stream Deck version of this plugin: the
`Lightroom/` namespace imports nothing from `PluginApi` - it only uses the
.NET base class library (`System.Net.WebSockets`, `System.Text.Json`,
`System.Threading`). This is not just tidiness: it is *why* this project
could be meaningfully verified at all in the environment it was built in
(see "Build verification" below).

## Persistent state on disk

`~/Library/Application Support/com.keelan182.lightroom-presets-mx/`:

| File | Contents | Written by |
|---|---|---|
| `presets-cache.json` | Last known presets + a timestamp | `PresetManager.RefreshAsync()` |
| `connection-state.json` | The pairing client GUID Lightroom issued | `LightroomConnection` after a successful register |

Per-button preset assignment lives inside the ActionEditor's own control
state for that button instance - the Logi Plugin Service persists this
itself as part of a device's profile, the same way Stream Deck persists
per-action settings. This plugin never has to serialize per-button
assignments itself.

## Known platform differences from the Stream Deck version

Both versions share the exact same `Lightroom/` protocol logic. What
differs is what each hardware SDK's configuration UI can currently do:

- **No custom free-text title field.** The Stream Deck version lets you
  type an arbitrary custom title; this version's `TitleMode` control only
  offers "preset name" or "preset name + connection status" (both
  confirmed rendered live via `ApplyPresetCommand.GetCommandDisplayName` -
  see "Build verification" below). A real `ActionEditorTextbox` control
  was confirmed to exist during that same verification pass, so a
  free-text option could be added the same way; it just wasn't in this
  first pass.
- **No favorites / search UI.** The Stream Deck version groups a
  "★ Favorites" section using its own global settings API. `Plugin.SetPluginSetting`/`TryGetPluginSetting`
  are confirmed present and exactly this shape in the real `PluginApi.dll`
  (see "Build verification"), so this is a scoping choice under time
  constraints, not a hard SDK limitation - a reasonable thing to add
  later.
- **`ApplyPresetCommand.RunCommand` blocks briefly** (up to 10s) on the
  actual apply-preset outcome before returning its success/failure boolean,
  because that boolean is the only feedback channel confirmed available on
  `ActionEditorCommand` - unlike the Stream Deck version, which can call
  `showOk()`/`showAlert()`/`setTitle()` independently of the key-press
  handler's return value.

## Build verification

This project's C# was written in a Linux sandbox with no macOS, no Logi
Options+, and no MX Creative Console available - but it has still been
compiled against the **real** `PluginApi.dll`, not just a guess. Here's
exactly how, in order:

1. **The entire `Lightroom/` namespace** (the WebSocket client, JSON
   parsing, reconnect logic, preset cache, and typed error handling) has
   zero dependency on `PluginApi`, so it could be compiled *and run* for
   real with the .NET SDK on its own - including letting its reconnect
   loop execute against a real (absent) Lightroom process for several
   seconds and confirming the typed error path fires correctly. This is
   the part of the project doing the trickiest work (async networking,
   concurrency, defensive JSON parsing).
2. **The `PluginApi`-dependent shell** was first written against a
   hand-written stub assembly reconstructing every `PluginApi`
   type/method this project calls, built from two real sources:
   Logitech's own current, official `Logitech/actions-sdk` DemoPlugin, and
   the third-party `loupedeck-lightroom-cc` plugin (for the
   `ActionEditorCommand`/`ActionEditorListbox` dropdown pattern - see
   docs/PROTOCOL.md). This caught wrong argument counts/types early, but
   couldn't catch anything the stub itself guessed wrong.
3. **The user then supplied the real `PluginApi.dll`** (v6.4.1.3246) from
   their own installed Logi Plugin Service, extracted from
   `/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/`.
   Reflecting on it directly (via `System.Reflection.MetadataLoadContext`,
   which reads assembly metadata without executing any of its code)
   turned up several real, concrete differences from the stub:
   - `ActionEditorCommand`'s constructor requires a `DeviceType`
     argument (`ActionEditorCommand(DeviceType supportedDevices)`) -
     fixed by calling `base(DeviceType.All)`.
   - `PluginDynamicCommand`'s 3-argument constructor the public DemoPlugin
     sample uses doesn't exist in this version - it takes a `DeviceType`
     as a required 4th argument too.
   - `ActionEditorListbox`'s constructor takes three arguments
     (`name, labelText, description`), not two.
   - `ActionEditorCommand` (via its `ActionEditorAction` base) does expose
     `protected virtual String GetCommandDisplayName(ActionEditorActionParameters actionParameters)`
     and a matching `GetCommandImage` overload - confirmed real and now
     wired up in `ApplyPresetCommand.GetCommandDisplayName`, resolving
     what was previously an "unverified, not implemented" gap.
   - The installed assembly itself targets **.NET 10** (it references
     `System.Runtime, Version=10.0.0.0`), not the `net8.0` Logitech's own
     public GitHub sample project declares. This repo's
     `<TargetFramework>` was updated to `net10.0` to match - a build
     against a "correct-looking" `net8.0` project would have failed with
     a framework-version conflict despite every method signature being
     right.
   - Everything else (`ActionEditorActionParameters.TryGetString`,
     `ActionEditorState.GetControlValue`/`SetDisplayName`,
     `ClientApplication.GetProcessName`/`GetBundleName`, the
     `PluginLog`/`PluginResources` boilerplate, `RunCommand`'s signature
     itself) matched the stub exactly.
4. **After fixing those, the project built with the real `PluginApi.dll`
   with zero warnings and zero errors.** The DLL was used only locally for
   this verification and was not committed to the repo or redistributed -
   it's Logitech's proprietary file, not this project's to distribute.

What this **still doesn't** prove: that the plugin actually loads and runs
correctly inside a live Logi Plugin Service process, that its actions
appear and behave correctly in Options+'s UI, or that a physical MX
Creative Console key press actually applies a Lightroom preset. Those are
the one category of thing that genuinely needs the physical hardware and
a live session - see docs/TESTING.md's critical acceptance test.

## macOS-only, no automation, no unnecessary native dependencies

- The only OS-level integration beyond WebSocket networking is a single,
  fixed, non-shell, argv-array `pgrep -f "Adobe Lightroom"` call
  (`ProcessStartInfo.ArgumentList`, never a shell string) used purely to
  distinguish "Lightroom isn't open" from "Lightroom is open but
  unreachable" - never to control or automate Lightroom itself.
- No Accessibility permissions, no UI scripting, no keystroke/mouse
  simulation anywhere in this codebase.
