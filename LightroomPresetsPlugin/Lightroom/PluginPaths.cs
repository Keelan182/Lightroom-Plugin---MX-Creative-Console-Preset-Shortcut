namespace Loupedeck.LightroomPresetsPlugin.Lightroom
{
    using System;
    using System.IO;

    // Per-user, macOS-appropriate directory for this plugin's own persisted
    // data (preset cache, pairing state). Kept outside the plugin bundle so
    // it survives plugin updates/reinstalls.
    public static class PluginPaths
    {
        private const String AppSupportDirName = "com.keelan182.lightroom-presets-mx";

        public static String AppSupportDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", AppSupportDirName);

        public static String PresetsCachePath => Path.Combine(AppSupportDirectory, "presets-cache.json");

        public static String ConnectionStatePath => Path.Combine(AppSupportDirectory, "connection-state.json");

        // Best-effort location of the folder Lightroom itself writes connection
        // bookkeeping into. Adobe does not publish a schema for this file, so it
        // is only ever used as an optional hint for discovering a non-default
        // port - the plugin always falls back to the documented default
        // (127.0.0.1:7682) when this can't be read or parsed.
        public static String LightroomConnectionsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Adobe", "Lightroom CC", "Connections");
    }
}
