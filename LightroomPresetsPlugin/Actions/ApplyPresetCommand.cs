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
    // Every signature in this file (ActionEditorCommand's constructor,
    // ActionEditorListbox's constructor, RunCommand/GetCommandDisplayName's
    // exact parameter types) was confirmed against a real PluginApi.dll
    // (v6.4.1.3246) via reflection, not guessed - see docs/PROTOCOL.md.
    public class ApplyPresetCommand : ActionEditorCommand
    {
        private const String PresetControlName = "PresetSelection";
        private const String TitleModeControlName = "TitleMode";

        private const String TitleModePresetOnly = "preset";
        private const String TitleModePresetAndStatus = "preset-and-status";

        public ApplyPresetCommand() : base(DeviceType.All)
        {
            this.Name = "ApplyPreset";
            this.DisplayName = "Apply Lightroom Preset";
            this.GroupName = "Lightroom Presets";
            this.Description = "Applies a chosen Lightroom Desktop/CC develop preset to the currently selected photo.";

            this.ActionEditor.AddControlEx(new ActionEditorListbox(name: PresetControlName, labelText: "Preset:", description: "The Lightroom preset to apply."));

            this.ActionEditor.AddControlEx(new ActionEditorListbox(name: TitleModeControlName, labelText: "Button title:", description: "What to display on the key."));

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

        protected override String GetCommandDisplayName(ActionEditorActionParameters actionParameters)
        {
            if (!actionParameters.TryGetString(PresetControlName, out var presetId) || presetId == "none" || String.IsNullOrEmpty(presetId))
            {
                return "Select\nPreset";
            }

            var preset = LightroomPresetsPlugin.PresetManager.FindById(presetId);
            var name = preset.HasValue ? preset.Value.Name : "Preset\nNot Found";

            actionParameters.TryGetString(TitleModeControlName, out var titleMode);
            if (titleMode == TitleModePresetAndStatus)
            {
                return $"{name}\n({LightroomPresetsPlugin.Connection.Status})";
            }

            return name;
        }
    }
}
