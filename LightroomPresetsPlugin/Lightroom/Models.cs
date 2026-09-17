namespace Loupedeck.LightroomPresetsPlugin.Lightroom
{
    using System;

    // Connection state between this plugin and Lightroom Desktop/CC's local
    // "External Controller API" (ws://127.0.0.1:7682 by default).
    public enum LightroomConnectionStatus
    {
        NotRunning,
        Connecting,
        Connected,
        Disconnected,
        Unresponsive
    }

    public struct LightroomPreset
    {
        public String Id;
        public String Name;

        public LightroomPreset(String id, String name)
        {
            this.Id = id;
            this.Name = name;
        }
    }

    public enum PresetApplicationErrorCode
    {
        NotRunning,
        Disconnected,
        Unresponsive,
        PresetNotFound,
        NoPresetConfigured,
        LightroomError
    }

    public class PresetApplicationException : Exception
    {
        public PresetApplicationErrorCode ErrorCode { get; }

        public PresetApplicationException(PresetApplicationErrorCode errorCode, String message) : base(message)
        {
            this.ErrorCode = errorCode;
        }
    }
}
