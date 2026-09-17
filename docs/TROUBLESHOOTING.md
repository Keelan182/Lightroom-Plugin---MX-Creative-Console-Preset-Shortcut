# Troubleshooting

## Build fails with a `PluginApi` type/member not found, or a .NET version conflict

This project has already been build-verified (0 errors) against a real
`PluginApi.dll`, version **6.4.1.3246**, targeting **.NET 10** (see
docs/ARCHITECTURE.md's "Build verification" section) - so a failure here
most likely means your installed Logi Plugin Service is a different
version than that one. Two common cases:

- **A framework-version error** (e.g. "uses 'System.Runtime, Version=X.0.0.0'
  which has a higher/lower version than referenced assembly"): your Logi
  Plugin Service targets a different .NET version than 10. Open
  `LightroomPresetsPlugin/LightroomPresetsPlugin.csproj` and change
  `<TargetFramework>net10.0</TargetFramework>` to match (e.g. `net8.0`),
  then rebuild. Install the matching .NET SDK first if needed
  (`dotnet --list-sdks` shows what you have).
- **A missing type/member error**: the exact type or method named in the
  error has a different shape in your installed version than v6.4.1.3246.
  Use a decompiler (e.g. ILSpy) on your installed
  `/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/PluginApi.dll`
  to see its real current signature, and adjust the call site in
  `LightroomPresetsPlugin/Actions/ApplyPresetCommand.cs` or
  `RefreshPresetsCommand.cs` to match. If you can, consider sharing that
  `PluginApi.dll` back so this project can be re-verified against your
  version too, the same way it was against v6.4.1.3246.

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

This project was written in a Linux sandbox with **no access to macOS,
Logi Options+/the Logi Plugin Service, an MX Creative Console, or a
licensed Lightroom install** - but it has been compiled clean (0 errors)
against a real `PluginApi.dll` supplied from an actual Logi Plugin Service
install. See docs/ARCHITECTURE.md's "Build verification" section for
exactly what that did and didn't prove. The practical upshot: both the
Lightroom communication layer *and* the plugin-hosting shell are
genuinely build-verified now, not just carefully written - what's left
unverified is specifically the things only a live Logi Plugin Service
process and physical hardware can confirm (the plugin actually loading,
its actions appearing correctly in Options+, and a real key press applying
a real preset). Please report back anything that doesn't match what you
observe there, and whether the `pgrep -f "Adobe Lightroom"` process check
actually matches Lightroom Desktop's real process name on your Mac (run
`ps aux | grep -i lightroom` while it's open to check, and update
`LIGHTROOM_PROCESS_PATTERN` in `Lightroom/LightroomConnection.cs` if not).
