namespace Loupedeck.LightroomPresetsPlugin
{
    using System;
    using Loupedeck.LightroomPresetsPlugin.Lightroom;

    // Composition root for the plugin. Owns the single LightroomConnection,
    // PresetManager and PresetApplicationService shared by every action
    // instance - mirroring the architecture used in the companion Stream
    // Deck version of this plugin (see docs/ARCHITECTURE.md).
    public class LightroomPresetsPlugin : Plugin
    {
        public static LightroomConnection Connection { get; private set; }

        public static PresetManager PresetManager { get; private set; }

        public static PresetApplicationService PresetApplication { get; private set; }

        // This plugin talks to Lightroom entirely over its own WebSocket
        // connection (see Lightroom/LightroomConnection.cs) rather than
        // through the Loupedeck host's built-in "linked application" state
        // machine - the same choice the reference plugin this was studied
        // from made, and one we keep for the same reason: the exact
        // semantics of ClientApplicationStatus/GetBundleName for a
        // Creative-Cloud app like Lightroom (as opposed to a classic
        // Windows/Mac executable) were not something this project could
        // verify. See docs/ARCHITECTURE.md.
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public LightroomPresetsPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            Connection = new LightroomConnection(PluginLog.Bridge);
            PresetManager = new PresetManager(Connection, PluginLog.Bridge);
            PresetApplication = new PresetApplicationService(Connection, PresetManager, PluginLog.Bridge);

            // Fire-and-forget: Load() must return promptly, and both of these
            // are safe to run in the background (they only ever improve
            // state over time - see docs/ARCHITECTURE.md for the connection
            // lifecycle).
            _ = PresetManager.LoadCacheFromDiskAsync();
            _ = Connection.StartAsync();
        }

        public override void Unload()
        {
            try
            {
                Connection?.StopAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Error while stopping the Lightroom connection during unload");
            }
        }
    }
}
