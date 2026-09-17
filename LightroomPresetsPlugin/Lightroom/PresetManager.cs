namespace Loupedeck.LightroomPresetsPlugin.Lightroom
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;

    public class RefreshResult
    {
        public List<LightroomPreset> Presets;
        public List<String> RemovedIds;
    }

    // Discovers, caches and resolves Lightroom develop presets.
    //
    // Lightroom's External Controller API only exposes a flat list of preset
    // ids plus a name-per-id lookup - there is no folder/group metadata
    // available over this API, so no hierarchy is reconstructed here (see
    // docs/PROTOCOL.md).
    public class PresetManager
    {
        private const Int32 NameFetchConcurrency = 4;
        private const Int32 NameFetchStaggerMs = 15;

        private readonly LightroomConnection _connection;
        private readonly Action<String, String> _log;
        private List<LightroomPreset> _presets = new List<LightroomPreset>();
        private Boolean _loadedFromDisk;

        public PresetManager(LightroomConnection connection, Action<String, String> log = null)
        {
            this._connection = connection;
            this._log = log ?? ((level, message) => { });
        }

        public async Task LoadCacheFromDiskAsync()
        {
            if (this._loadedFromDisk)
            {
                return;
            }
            this._loadedFromDisk = true;

            try
            {
                var path = PluginPaths.PresetsCachePath;
                if (!File.Exists(path))
                {
                    this._log("info", "No cached preset list found on disk yet; run \"Refresh Lightroom Presets\" once Lightroom is connected.");
                    return;
                }

                var json = await File.ReadAllTextAsync(path);
                using (var doc = JsonDocument.Parse(json))
                {
                    var loaded = new List<LightroomPreset>();
                    if (doc.RootElement.TryGetProperty("presets", out var presetsElement) && presetsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in presetsElement.EnumerateArray())
                        {
                            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                            var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                            if (!String.IsNullOrEmpty(id) && !String.IsNullOrEmpty(name))
                            {
                                loaded.Add(new LightroomPreset(id, name));
                            }
                        }
                    }
                    this._presets = loaded;
                    this._log("info", $"Loaded {this._presets.Count} cached presets from disk");
                }
            }
            catch (Exception ex)
            {
                this._log("warn", $"Failed to load preset cache from disk: {ex.Message}");
            }
        }

        public List<LightroomPreset> GetPresets() => this._presets;

        public LightroomPreset? FindById(String id)
        {
            foreach (var preset in this._presets)
            {
                if (preset.Id == id)
                {
                    return preset;
                }
            }
            return null;
        }

        public List<LightroomPreset> Search(String query)
        {
            if (String.IsNullOrWhiteSpace(query))
            {
                return this._presets;
            }
            var needle = query.Trim().ToLowerInvariant();
            return this._presets.Where(preset => preset.Name.ToLowerInvariant().Contains(needle)).ToList();
        }

        // Re-discovers every preset from Lightroom: retrieves ids via
        // "getPresetIDs", resolves each id's name via "getPresetName", caches
        // the result to disk, and reports which previously-known ids
        // disappeared so the caller can flag stale button assignments.
        public async Task<RefreshResult> RefreshAsync()
        {
            this._log("info", "Requesting preset list from Lightroom (getPresetIDs)");
            var idsResponse = await this._connection.SendRequestAsync("getPresetIDs");
            var ids = ExtractPresetIds(idsResponse);
            this._log("info", $"Lightroom reported {ids.Count} preset id(s); resolving names");

            var previousIds = new HashSet<String>(this._presets.Select(p => p.Id));
            var resolved = new List<LightroomPreset>();

            for (var start = 0; start < ids.Count; start += NameFetchConcurrency)
            {
                var batch = ids.Skip(start).Take(NameFetchConcurrency).ToList();
                var tasks = batch.Select(async id =>
                {
                    try
                    {
                        var nameResponse = await this._connection.SendRequestAsync("getPresetName", new Object[] { id });
                        var name = ExtractPresetName(nameResponse);
                        return String.IsNullOrEmpty(name) ? (LightroomPreset?)null : new LightroomPreset(id, name);
                    }
                    catch (Exception ex)
                    {
                        this._log("warn", $"Failed to resolve name for preset {id}: {ex.Message}");
                        return (LightroomPreset?)null;
                    }
                });

                var results = await Task.WhenAll(tasks);
                foreach (var preset in results)
                {
                    if (preset.HasValue)
                    {
                        resolved.Add(preset.Value);
                    }
                }

                if (start + NameFetchConcurrency < ids.Count)
                {
                    await Task.Delay(NameFetchStaggerMs);
                }
            }

            resolved.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            this._presets = resolved;

            var currentIds = new HashSet<String>(resolved.Select(p => p.Id));
            var removedIds = previousIds.Where(id => !currentIds.Contains(id)).ToList();

            await this.SaveCacheToDiskAsync();
            this._log("info", $"Preset refresh complete: {resolved.Count} preset(s) cached, {removedIds.Count} removed since last refresh");

            return new RefreshResult { Presets = resolved, RemovedIds = removedIds };
        }

        private async Task SaveCacheToDiskAsync()
        {
            try
            {
                Directory.CreateDirectory(PluginPaths.AppSupportDirectory);
                var payload = new
                {
                    updatedAt = DateTime.UtcNow.ToString("o"),
                    presets = this._presets.Select(p => new { id = p.Id, name = p.Name }).ToArray()
                };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(PluginPaths.PresetsCachePath, json);
            }
            catch (Exception ex)
            {
                this._log("error", $"Failed to write preset cache to disk: {ex.Message}");
            }
        }

        // Lightroom's "getPresetIDs" response has been observed to arrive as a
        // direct array of id strings, a JSON-encoded string containing that
        // array, an object wrapping the array in an unpredictable property
        // name, or a single nested array-of-arrays. This defensively unwraps
        // all of those shapes down to a flat list of strings.
        public static List<String> ExtractPresetIds(LightroomResponse response)
        {
            var ids = new List<String>();
            if (!response.HasResponse)
            {
                return ids;
            }

            foreach (var value in UnwrapToValues(response.Response))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var s = value.GetString();
                    if (!String.IsNullOrEmpty(s))
                    {
                        ids.Add(s);
                    }
                }
            }
            return ids;
        }

        public static String ExtractPresetName(LightroomResponse response)
        {
            if (!response.HasResponse)
            {
                return null;
            }

            var element = response.Response;
            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
            if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0 && element[0].ValueKind == JsonValueKind.String)
            {
                return element[0].GetString();
            }
            return null;
        }

        private static IEnumerable<JsonElement> UnwrapToValues(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString();
                if (!String.IsNullOrEmpty(text))
                {
                    JsonDocument parsed = null;
                    try
                    {
                        parsed = JsonDocument.Parse(text);
                    }
                    catch (JsonException)
                    {
                        yield break;
                    }
                    using (parsed)
                    {
                        foreach (var value in UnwrapToValues(parsed.RootElement.Clone()))
                        {
                            yield return value;
                        }
                    }
                }
                yield break;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var nested in UnwrapToValues(item))
                        {
                            yield return nested;
                        }
                    }
                    else
                    {
                        yield return item;
                    }
                }
                yield break;
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var value in UnwrapToValues(property.Value))
                        {
                            yield return value;
                        }
                        yield break;
                    }
                }
            }
        }
    }
}
