# Lightroom Desktop/CC External Controller API - protocol notes

This is the same protocol research done for the companion Stream Deck
version of this plugin - Lightroom doesn't know or care what hardware is
talking to it, so none of this needed to be re-derived. It's reproduced
here so this repo is self-contained.

## What this is (and isn't)

- It **is** a feature Adobe ships inside Lightroom Desktop/CC itself: a
  local WebSocket server the app starts when you enable **Preferences >
  Interface > "Enable external controllers"**.
- It **is not** Lightroom Classic, the Lightroom Classic SDK, or the cloud
  "Lightroom API" / "Lightroom Partner APIs" (the OAuth REST API for
  Creative Cloud-stored photos at `developer.adobe.com/lightroom`).
- Adobe has not published a public schema for this local API. Everything
  below marked "observed" comes from third-party reverse engineering.

## Endpoint

- Default: `ws://127.0.0.1:7682`.
- If unavailable, Lightroom picks another port and records it somewhere
  under `~/Library/Application Support/Adobe/Lightroom CC/Connections` on
  macOS. This plugin best-effort-scans that folder for a `*.json` file with
  a `port`/`Port`/`websocketPort`/`webSocketPort` key
  (`Lightroom/LightroomConnection.cs#DetectPortAsync`), falling back to 7682.

## Message envelope

```json
{ "requestId": "<uuid>", "object": null, "message": "<command>", "params": [] }
```

Responses correlate back via `requestId`:

```json
{ "requestId": "<uuid>", "success": true, "response": <any> }
```

`LightroomConnection` correlates concurrent in-flight requests using a
`ConcurrentDictionary<string, TaskCompletionSource<LightroomResponse>>`
keyed by `requestId`, with a real WebSocket receive loop that reassembles
fragmented frames before parsing - not a naive "send then read the very
next frame" approach (which only works if requests are never concurrent).

## Pairing / registration

```json
{ "requestId": "<uuid>", "object": null, "message": "register", "params": ["<app name>", "<app version>", <previous client GUID or null>] }
```

The first time an unrecognized `(app name, app version)` registers,
Lightroom shows a pairing dialog and doesn't respond until the user clicks
**Allow**. On success, `response[0]` is a client GUID; resending it on a
later `register` call appears to let Lightroom recognize a previously
approved client (undocumented behavior, may change). This plugin persists
that GUID to
`~/Library/Application Support/com.keelan182.lightroom-presets-mx/connection-state.json`
so pairing survives plugin/app restarts, not just process lifetime.

Because an "Allow" click can take any amount of time, `register` uses a
longer timeout (12s) than ordinary requests (8s), and a distinct
`Unresponsive` connection status (rather than a generic failure) so the UI
can hint at checking for an open pairing dialog.

## Preset discovery

- `getPresetIDs` (no params) - the response shape is inconsistent: a plain
  array of id strings, a JSON-encoded string containing that array, an
  array containing one nested array of ids, or an object with the array
  under an unpredictable property name. `PresetManager.ExtractPresetIds()`
  defensively unwraps all of these.
- `getPresetName` with `params: [presetId]` - a name string, sometimes
  wrapped in a 1-element array.

**There is no folder/group/category field anywhere in these responses.**
Presets are a genuinely flat list. This plugin presents them
alphabetically sorted, with no attempt to reconstruct or invent a folder
hierarchy Lightroom's API doesn't actually provide.

Because each name needs its own round trip, `PresetManager.RefreshAsync()`
resolves names with bounded concurrency (4 at a time, 15ms stagger)
rather than a fully serial one-at-a-time approach.

## Applying a preset

`applyPreset` with `params: [presetId]`. There is no documented, structured
error for "no photo is selected" - a failure surfaces only as a missing
response or `success: false`. `PresetApplicationService` reports this as a
generic "Lightroom did not confirm applying this preset" rather than
claiming to detect that specific case (see docs/TROUBLESHOOTING.md).

## Prior art / attribution

Understanding of this protocol was built by reading the publicly available
source of **adamkarnowka/loupedeck-lightroom-cc**
(https://github.com/adamkarnowka/loupedeck-lightroom-cc), a third-party
Loupedeck plugin for this exact device family, plus public discussion of
Lightroom's "Enable third-party controllers" feature. No source code from
that project is included here - this plugin's C#
(`LightroomPresetsPlugin/Lightroom/`) was written from scratch against the
protocol *facts* above, which are interoperability information rather than
copyrightable expression.

The `ApplyPresetCommand` action's use of `ActionEditorCommand` +
`ActionEditorListbox` (`AddControlEx`, `ListboxItemsRequested`,
`ControlValueChanged`, `ActionEditorActionParameters.TryGetString`) also
follows the shape of that same reference project's `ApplyPresetCommand.cs`,
since it's the only real-world example available of this exact
configurable-dropdown pattern on this SDK. One deliberate change from that
reference: its `RunCommand` started the preset-apply as fire-and-forget and
always returned `true` immediately, so the button's own success/failure
flash never reflected whether the preset actually applied. This project's
`RunCommand` waits (up to 10s) for the real outcome before returning, so
success/failure feedback is accurate.

The loupedeck-lightroom-cc README states it is MIT-licensed, but the
repository had no LICENSE file at the time of this review, so nothing from
it has been copied regardless - see LICENSE.
