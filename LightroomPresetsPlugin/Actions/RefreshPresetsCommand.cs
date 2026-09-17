namespace Loupedeck.LightroomPresetsPlugin.Actions
{
    using System;
    using System.Threading.Tasks;

    // "Refresh Lightroom Presets" - a plain command (no per-button
    // configuration needed), so this uses PluginDynamicCommand directly
    // rather than ActionEditorCommand. Confirmed against Logitech's own
    // current DemoPlugin sample (ThumbUpDownCommand.cs): GetCommandImage +
    // ActionImageChanged is a verified, current way to show state-dependent
    // artwork on the button face.
    public class RefreshPresetsCommand : PluginDynamicCommand
    {
        private enum State
        {
            Idle,
            Success,
            Error
        }

        private State _state = State.Idle;
        private readonly String _successImagePath;
        private readonly String _errorImagePath;

        public RefreshPresetsCommand()
            : base(displayName: "Refresh Lightroom Presets", description: "Re-discovers presets from Lightroom Desktop/CC and updates every button's cached preset list.", groupName: "Lightroom Presets")
        {
            this._successImagePath = PluginResources.FindFile("Success.png");
            this._errorImagePath = PluginResources.FindFile("Error.png");
        }

        protected override void RunCommand(String actionParameter)
        {
            if (LightroomPresetsPlugin.Connection.Status != Lightroom.LightroomConnectionStatus.Connected)
            {
                PluginLog.Warning($"Refresh requested but Lightroom is not connected (status: {LightroomPresetsPlugin.Connection.Status})");
                this.SetState(State.Error);
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var result = await LightroomPresetsPlugin.PresetManager.RefreshAsync();
                    PluginLog.Info($"Refreshed {result.Presets.Count} presets ({result.RemovedIds.Count} removed since last refresh)");

                    if (result.RemovedIds.Count > 0)
                    {
                        PluginLog.Warning($"{result.RemovedIds.Count} previously-cached preset id(s) no longer exist in Lightroom. Buttons using them will fail with \"Preset not found\" until reassigned.");
                    }

                    this.SetState(State.Success);
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Preset refresh failed");
                    this.SetState(State.Error);
                }
            });
        }

        private void SetState(State state)
        {
            this._state = state;
            this.ActionImageChanged();

            // Revert to the default artwork after a moment so the
            // success/error glyph reads as a momentary confirmation rather
            // than a persistent status indicator.
            Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ =>
            {
                this._state = State.Idle;
                this.ActionImageChanged();
            });
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            switch (this._state)
            {
                case State.Success:
                    return PluginResources.ReadImage(this._successImagePath);
                case State.Error:
                    return PluginResources.ReadImage(this._errorImagePath);
                default:
                    return null; // Falls back to the default icon declared in package/metadata.
            }
        }
    }
}
