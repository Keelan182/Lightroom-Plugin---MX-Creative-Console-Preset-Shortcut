namespace Loupedeck.LightroomPresetsPlugin.Lightroom
{
    using System;
    using System.Threading.Tasks;

    // Applies a cached preset id to whatever photo is currently selected in
    // Lightroom. Kept separate from LightroomConnection so the connection
    // layer only knows about the wire protocol, while this layer knows about
    // preset-specific validation and user-facing error mapping.
    public class PresetApplicationService
    {
        private readonly LightroomConnection _connection;
        private readonly PresetManager _presetManager;
        private readonly Action<String, String> _log;

        public PresetApplicationService(LightroomConnection connection, PresetManager presetManager, Action<String, String> log = null)
        {
            this._connection = connection;
            this._presetManager = presetManager;
            this._log = log ?? ((level, message) => { });
        }

        public async Task ApplyAsync(String presetId)
        {
            if (String.IsNullOrEmpty(presetId))
            {
                throw new PresetApplicationException(PresetApplicationErrorCode.NoPresetConfigured, "No preset is configured for this button yet.");
            }

            var status = this._connection.Status;
            if (status == LightroomConnectionStatus.NotRunning)
            {
                throw new PresetApplicationException(PresetApplicationErrorCode.NotRunning, "Lightroom is not running. Open Lightroom Desktop and try again.");
            }
            if (status == LightroomConnectionStatus.Disconnected)
            {
                throw new PresetApplicationException(PresetApplicationErrorCode.Disconnected, "Unable to connect to Lightroom. Check Lightroom's External Controller settings (Preferences > Interface).");
            }
            if (status == LightroomConnectionStatus.Unresponsive)
            {
                throw new PresetApplicationException(PresetApplicationErrorCode.Unresponsive, "Lightroom is not responding. If a pairing dialog is open in Lightroom, click Allow.");
            }

            var known = this._presetManager.FindById(presetId);
            if (!known.HasValue)
            {
                throw new PresetApplicationException(PresetApplicationErrorCode.PresetNotFound, $"Preset not found: {presetId}. Open the action settings and select a preset, or run \"Refresh Lightroom Presets\".");
            }

            this._log("info", $"Applying preset \"{known.Value.Name}\" ({presetId})");

            try
            {
                var response = await this._connection.SendRequestAsync("applyPreset", new Object[] { presetId });
                if (!response.Success)
                {
                    throw new PresetApplicationException(PresetApplicationErrorCode.LightroomError, $"Lightroom rejected applyPreset for \"{known.Value.Name}\"");
                }
                this._log("info", $"Preset \"{known.Value.Name}\" applied");
            }
            catch (PresetApplicationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                this._log("error", $"applyPreset failed for \"{known.Value.Name}\": {ex.Message}");
                throw new PresetApplicationException(PresetApplicationErrorCode.LightroomError, $"Lightroom did not confirm applying \"{known.Value.Name}\": {ex.Message}");
            }
        }
    }
}
