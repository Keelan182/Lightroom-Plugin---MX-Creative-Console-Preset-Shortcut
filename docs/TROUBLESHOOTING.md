# Troubleshooting

## Build fails with a `PluginApi` type/member not found

This means the real `PluginApi.dll` on your Mac doesn't match the stub
signatures this project verified against (see docs/ARCHITECTURE.md,
"Build verification"). The error will name the exact type or member -
common candidates to double check first:

- `ActionEditorCommand`, `ActionEditorListbox`,
  `ActionEditorListboxItemsRequestedEventArgs`,
  `ActionEditorControlValueChangedEventArgs`, `ActionEditorActionParameters`
  in `LightroomPresetsPlugin/Actions/ApplyPresetCommand.cs` - these were
  confirmed from a third-party plugin's source, not Logitech's own official
  sample, so they're the most likely to have shifted.
- If only `ApplyPresetCommand.cs` fails: you can still build and use
  `RefreshPresetsCommand.cs` (confirmed against Logitech's own current
  DemoPlugin) while you fix the dropdown action separately.

Use a decompiler (e.g. ILSpy) on your installed
`/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/PluginApi.dll`
to see the exact current signatures, and adjust the call site to match.

## Build fails at the `PostBuild` step trying to write a `.link` file

That step assumes
`~/Library/Application Support/Logi/LogiPluginService/Plugins/` already
exists, which is normally created the first time you run Logi Options+.
Open Logi Options+ once, then rebuild. This step is a dev convenience for
live-linking during development (`npm run link`-equivalent) - it does not
affect the packaged `.lplug4` file at all, so packaging/distribution works
regardless.

## "Button shows an error / red flash on press"

`ApplyPresetCommand.RunCommand` returns `false` (which the host renders as
a failure flash) whenever `PresetApplicationService.ApplyAsync` throws.
Check the plugin log for the specific `PresetApplicationException`
message - it will say one of:

- **"Lightroom is not running."** - open Lightroom Desktop/CC.
- **"Unable to connect to Lightroom. Check Lightroom's External Controller
  settings."** - go to Lightroom's Preferences > Interface and enable
  "Enable external controllers", then restart Lightroom.
- **"Lightroom is not responding."** - a pairing dialog may be open in
  Lightroom; switch to it and click Allow.
- **"Preset not found: ..."** - the assigned preset was renamed or deleted;
  reassign it in the button's settings, or press "Refresh Lightroom
  Presets" first to see which buttons are affected.
- **"No preset is configured for this button yet."** - open the button's
  settings and pick one from the dropdown.

## Finding the plugin's log file

Loupedeck/Logi plugin logs are written by the Logi Plugin Service itself,
typically under
`~/Library/Logs/LogiPluginService/` or alongside the plugin's own installed
copy under
`~/Library/Application Support/Logi/LogiPluginService/Plugins/LightroomPresetsPlugin/`
(exact path can shift between Logi Plugin Service versions - check both, or
search `~/Library` for `LightroomPresetsPlugin*.log`). Every connection
status change, preset discovery run, and apply-preset attempt is logged
through `PluginLog`, matching what's logged in the companion Stream Deck
version.

## No presets in the dropdown

Press **Refresh Lightroom Presets** first (it must be assigned to a
button, or run once via whatever the Logi Plugin Service's own
test/trigger mechanism is in your installed version). The dropdown reads
from `PresetManager`'s in-memory cache, which starts empty until a refresh
succeeds or a prior cache file is found on disk.

## This plugin cannot detect "no photo selected" precisely

Same limitation as the Stream Deck version - see docs/PROTOCOL.md. If
`applyPreset` fails for a reason other than the ones this plugin can
positively identify, you'll see a generic failure rather than a specific
"no photo selected" message.

## Verifying this yourself

This project was built and compiled/run in a Linux sandbox with **no
access to macOS, Logi Options+/the Logi Plugin Service, an MX Creative
Console, or a licensed Lightroom install**. See docs/ARCHITECTURE.md's
"Build verification" section for exactly what was and wasn't proven. The
practical upshot: the Lightroom communication layer is genuinely tested
(compiled and run for real); the plugin-hosting shell (actions, manifest,
packaging) is careful, sourced from two real reference projects, but has
never been loaded by an actual Logi Plugin Service. Please report back
anything that doesn't match what you observe - especially any `PluginApi`
compile error, and whether the `pgrep -f "Adobe Lightroom"` process check
actually matches Lightroom Desktop's real process name on your Mac (run
`ps aux | grep -i lightroom` while it's open to check, and update
`LIGHTROOM_PROCESS_PATTERN` in `Lightroom/LightroomConnection.cs` if not).
