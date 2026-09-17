# Test plan

This project was built and verified in a Linux sandbox with no access to
macOS, Logi Options+, an MX Creative Console, or a licensed Lightroom
install. Everything below needs to be run by you. See
docs/ARCHITECTURE.md's "Build verification" section for exactly what was
already proven (the Lightroom protocol layer, compiled and run for real)
versus what could only be carefully written against reference sources (the
plugin-hosting shell).

## Build-time checks (already done)

- [x] `Lightroom/*.cs` compiles under the .NET 8 SDK with zero warnings/errors.
- [x] `Lightroom/*.cs` was actually **run** (not just compiled): the
      reconnect loop executed for real, correctly reported "not running"
      with no Lightroom process present, and `PresetApplicationService`
      threw the correct typed error.
- [x] The full plugin project (actions, manifest copying, embedded
      resources) compiles with zero errors against a hand-written stub
      reproducing every `PluginApi` signature this project calls.
- [x] Generated icons (`package/metadata/Icon256x256.png`,
      `images/Success.png`, `images/Error.png`) render correctly.

## First real build on your Mac

1. Install Logi Options+ (which installs the Logi Plugin Service) and open
   it at least once.
2. `dotnet build` from the repo root (or open `LightroomPresetsPlugin.sln`
   in Visual Studio/Rider).
3. If this fails on a specific `PluginApi` member, see
   docs/TROUBLESHOOTING.md - it means the real assembly's signature differs
   slightly from this project's best reconstruction of it.
4. On success, `npm run link`-equivalent for this SDK is the `.link` file
   the `PostBuild` target writes automatically during `dotnet build` - the
   plugin should appear in Options+'s device configuration screen under
   "All Actions" without a separate install step.

## Critical acceptance test (do this first, once it builds)

1. Build and link the plugin as above.
2. Open Lightroom Desktop/CC; enable Preferences > Interface > "Enable
   external controllers"; restart Lightroom.
3. Open a photo.
4. In Options+, assign "Apply Lightroom Preset" to an MX Creative Console
   key.
5. Configure it: confirm the preset dropdown populates with your real
   presets (if empty, assign and press "Refresh Lightroom Presets" first).
6. Pick a preset you can visually recognize.
7. Press the physical key.
8. **Verify Lightroom visibly re-renders the photo with that preset's
   look**, and the key shows a success flash rather than a failure flash.

If step 8 doesn't happen, check docs/TROUBLESHOOTING.md and the plugin log
before assuming the architecture is wrong.

## Connection

- [ ] Lightroom closed when the plugin loads -> apply attempts fail with
      "Lightroom is not running", no crash.
- [ ] Open Lightroom while the plugin is running -> connects within a few
      seconds without restarting anything.
- [ ] Quit Lightroom while connected -> falls back to "not running"; no
      runaway reconnect-attempt log spam (backoff should visibly increase,
      capped at 30s - check the log).
- [ ] Restart Lightroom -> plugin reconnects and re-registers; note whether
      a new pairing dialog appears (the persisted client GUID is meant to
      avoid this on later restarts, but this is unverified - see
      docs/PROTOCOL.md).
- [ ] Toggle "Enable external controllers" off while connected -> next
      reconnect attempt should read as "disconnected", not "not running".

## Presets

- [ ] A catalog with a single preset.
- [ ] 100+ presets - "Refresh Lightroom Presets" should complete in a
      reasonable time (4-way concurrent name lookups).
- [ ] Preset names with spaces, emoji, accents, `&`, quotes.
- [ ] Two presets sharing the same name (different ids) - the dropdown is
      keyed by id, but visually indistinguishable names are worth
      eyeballing.
- [ ] Presets organized into folders in Lightroom's own UI - confirm (per
      docs/PROTOCOL.md) this plugin's list stays flat; that's a known API
      limitation, not a bug.
- [ ] Delete an assigned preset, then refresh - the button relying on it
      should start failing with "Preset not found" (there is currently no
      cross-button "flag stale assignments" sweep like the Stream Deck
      version has - see docs/ARCHITECTURE.md's platform-differences note).
- [ ] Rename an assigned preset, then refresh - since the id is unchanged,
      the dropdown should show the new name on next open.

## MX Creative Console

- [ ] Multiple "Apply Lightroom Preset" keys, each with a different preset
      - confirm changing one key's assignment never affects another's.
- [ ] The same preset assigned to two different keys.
- [ ] Reassigning a key's preset takes effect on the very next press
      without needing to reopen Options+.
- [ ] Restart the Logi Plugin Service / reboot macOS - plugin reloads and
      reconnects without manual intervention once both apps are open.

## Lightroom content

- [ ] Apply to a RAW photo.
- [ ] Apply to a JPEG.
- [ ] Apply with nothing selected - note exactly what happens (see the
      "no photo selected" limitation in docs/PROTOCOL.md /
      docs/TROUBLESHOOTING.md) and report back what Lightroom actually
      returns, since that could tighten this in a future revision.

## Reporting results

Please note which items passed/failed and the exact log lines for any
failure, especially anything that reveals a `PluginApi` signature mismatch
or an incorrect assumption about `ActionEditorCommand`'s capabilities -
those are the parts of this project that could not be verified without a
real Mac and a real Logi Plugin Service installation.
