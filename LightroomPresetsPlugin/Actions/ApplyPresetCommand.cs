namespace Loupedeck.LightroomPresetsPlugin.Actions
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Loupedeck.LightroomPresetsPlugin.Lightroom;

    // "Apply Lightroom Preset" - the primary action. Uses the ActionEditor
    // control system (AddControlEx + a listbox) rather than
    // PluginDynamicCommand.AddParameter, because we need one configurable
    // dropdown per assigned button instance (the same UX Stream Deck calls a
    // "Property Inspector"), not a fixed set of named sub-commands.
    //
    // This exact class shape (ActionEditorCommand + ActionEditorListbox with
    // ListboxItemsRequested/ControlValueChanged, and RunCommand reading the
    // control's value via ActionEditorActionParameters.TryGetString) is
    // reproduced from a real, working third-party Lightroom plugin for this
    // same device family that this project studied - see docs/PROTOCOL.md
    // for the full attribution note. It was not possible to compile this
    // specific file in the environment this project was built in, because
    // PluginApi.dll (which defines ActionEditorCommand) only ships inside an
    // installed copy of the Logi Plugin Service app - see README.md.
    public class ApplyPresetCommand : ActionEditorCommand
    {
        private const String PresetControlName = "PresetSelection";
        private const String TitleModeControlName = "TitleMode";

        private const String TitleModePresetOnly = "preset";
        private const String TitleModePresetAndStatus = "preset-and-status";

        public ApplyPresetCommand()
        {
            this.Name = "ApplyPreset";
            this.DisplayName = "Apply Lightroom Preset";
            this.GroupName = "Lightroom Presets";
            this.Description = "Applies a chosen Lightroom Desktop/CC develop preset to the currently selected photo.";

            this.ActionEditor.AddControlEx(new ActionEditorListbox(name: PresetControlName, labelText: "Preset:"));

            this.ActionEditor.AddControlEx(new ActionEditorListbox(name: TitleModeControlName, labelText: "Button title:"));

            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;
            this.ActionEditor.ControlValueChanged += this.OnControlValueChanged;
        }

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName.EqualsNoCase(PresetControlName))
            {
                var presets = LightroomPresetsPlugin.PresetManager.GetPresets();

                if (presets == null || presets.Count == 0)
                {
                    e.AddItem(name: "none", displayName: "No presets cached - run \"Refresh Lightroom Presets\" first", description: null);
                    e.SetSelectedItemName("none");
                    return;
                }

                foreach (var preset in presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                {
                    e.AddItem(name: preset.Id, displayName: preset.Name, description: $"ID: {preset.Id}");
                }
            }
            else if (e.ControlName.EqualsNoCase(TitleModeControlName))
            {
                e.AddItem(name: TitleModePresetOnly, displayName: "Preset name", description: null);
                e.AddItem(name: TitleModePresetAndStatus, displayName: "Preset name + connection status", description: null);
                e.SetSelectedItemName(TitleModePresetOnly);
            }
        }

        private void OnControlValueChanged(Object sender, ActionEditorControlValueChangedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(PresetControlName))
            {
                return;
            }

            var presetId = e.ActionEditorState.GetControlValue(PresetControlName);

            if (!String.IsNullOrEmpty(presetId) && presetId != "none")
            {
                var preset = LightroomPresetsPlugin.PresetManager.FindById(presetId);
                e.ActionEditorState.SetDisplayName(preset.HasValue ? preset.Value.Name : "Apply Lightroom Preset");
            }
            else
            {
                e.ActionEditorState.SetDisplayName("Apply Lightroom Preset");
            }
        }

        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(PresetControlName, out var presetId) || presetId == "none")
            {
                PluginLog.Warning("Apply Lightroom Preset pressed with no preset configured yet.");
                return false;
            }

            // RunCommand must return promptly; the actual apply happens on a
            // background task and its outcome only reaches the log plus the
            // boolean return of this synchronous call, which the Loupedeck
            // host uses to flash success/failure on the physical button.
            // Because that flash needs the OUTCOME, not just "did we start
            // trying", we block briefly on the task here rather than firing
            // it and returning immediately - matching the same call/response
            // shape RunCommand's synchronous signature expects.
            try
            {
                Task.Run(() => LightroomPresetsPlugin.PresetApplication.ApplyAsync(presetId)).Wait(TimeSpan.FromSeconds(10));
                return true;
            }
            catch (AggregateException ex) when (ex.InnerException is PresetApplicationException appEx)
            {
                PluginLog.Warning($"Apply preset failed ({appEx.ErrorCode}): {appEx.Message}");
                return false;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Unexpected error applying Lightroom preset");
                return false;
            }
        }

        // NOTE: unlike the Stream Deck version of this plugin, this action
        // does not expose a per-button "custom title" free-text field, and
        // the "Button title" control above is not currently wired to
        // anything rendered on the physical button. Only ActionEditorListbox
        // was confirmed available while building this project (see
        // docs/PROTOCOL.md) - showing a live title on the device face
        // appears to need an override such as GetCommandDisplayName, which
        // is confirmed to exist on PluginDynamicCommand (see
        // Actions/RefreshPresetsCommand.cs) but was not confirmed on
        // ActionEditorCommand specifically without a local PluginApi.dll to
        // inspect. If your installed SDK version supports it, add:
        //   protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        // reading the preset name (and TitleMode, for the "+ connection
        // status" option) the same way RunCommand reads them above.
    }
}
