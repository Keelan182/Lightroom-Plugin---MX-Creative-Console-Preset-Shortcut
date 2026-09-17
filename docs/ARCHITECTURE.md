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

- **No custom title / connection-status glyph on the button face is wired
  up.** `ApplyPresetCommand` has a `TitleMode` control in its editor, but
  nothing currently reads it to render text on the device, because
  overriding a display-name method on `ActionEditorCommand` specifically
  (as opposed to `PluginDynamicCommand`, which is confirmed to support
  this) could not be verified in this project's build environment - see
  "Build verification" below. If your installed SDK version supports it,
  `Actions/ApplyPresetCommand.cs` has a comment marking exactly where to
  add it.
- **No favorites / search UI.** The Stream Deck version groups a
  "★ Favorites" section using its own global settings API. This project
  scoped that out rather than guess at an unverified
  `Plugin.SetPluginSetting`-based implementation under time constraints;
  the reference project's own `LightroomPlugin.cs` confirms
  `SetPluginSetting`/`TryGetPluginSetting` exist, so this is a reasonable
  thing to add later, not a hard SDK limitation.
- **`ApplyPresetCommand.RunCommand` blocks briefly** (up to 10s) on the
  actual apply-preset outcome before returning its success/failure boolean,
  because that boolean is the only feedback channel confirmed available on
  `ActionEditorCommand` - unlike the Stream Deck version, which can call
  `showOk()`/`showAlert()`/`setTitle()` independently of the key-press
  handler's return value.

## Build verification

This project's C# could not be compiled against the real `PluginApi.dll`
in the environment it was built in, because that assembly ships **inside
an installed copy of the Logi Plugin Service macOS app** - it is not on
NuGet and has no standalone download. What was actually done instead:

1. **The entire `Lightroom/` namespace** (the WebSocket client, JSON
   parsing, reconnect logic, preset cache, and typed error handling) has
   zero dependency on `PluginApi` and was compiled *and run* for real with
   the .NET 8 SDK, including letting its reconnect loop execute against a
   real (absent) Lightroom process for several seconds and confirming the
   typed error path fires correctly. This is the part of the project doing
   the trickiest work (async networking, concurrency, defensive JSON
   parsing), and it is genuinely verified, not just reviewed.
2. **The `PluginApi`-dependent shell** (`LightroomPresetsPlugin.cs`,
   `LightroomApplication.cs`, `PluginLog.cs`, `PluginResources.cs`,
   `Actions/*.cs`) was compiled against a hand-written stub assembly
   reproducing every `PluginApi` type/method signature this project calls,
   built from two real sources: Logitech's own current, official
   `Logitech/actions-sdk` DemoPlugin (confirms `Plugin`, `ClientApplication`,
   `PluginDynamicCommand`, `PluginLog`/`PluginResources` boilerplate,
   `GetCommandImage`/`ActionImageChanged`), and the third-party
   `loupedeck-lightroom-cc` plugin (confirms `ActionEditorCommand`,
   `ActionEditorListbox`, and their event/parameter shapes). Every call
   site compiled clean against that stub - this catches wrong argument
   counts/types and incorrect overrides, but it *cannot* catch a
   fundamental API difference the stub itself got wrong (since the stub's
   shape is this project's own best reconstruction, not the real
   assembly).
3. What this **does not** prove: that the real `PluginApi.dll` matches the
   stub exactly, or that the plugin actually loads and runs correctly
   inside a real Logi Plugin Service process, or that a physical MX
   Creative Console button press actually applies a Lightroom preset. See
   docs/TESTING.md for exactly what to check on your own Mac, and please
   report back anything that doesn't match (particularly any compile error
   naming a `PluginApi` type/member, which would mean the stub's guess
   about that member's exact signature was wrong).

## macOS-only, no automation, no unnecessary native dependencies

- The only OS-level integration beyond WebSocket networking is a single,
  fixed, non-shell, argv-array `pgrep -f "Adobe Lightroom"` call
  (`ProcessStartInfo.ArgumentList`, never a shell string) used purely to
  distinguish "Lightroom isn't open" from "Lightroom is open but
  unreachable" - never to control or automate Lightroom itself.
- No Accessibility permissions, no UI scripting, no keystroke/mouse
  simulation anywhere in this codebase.
