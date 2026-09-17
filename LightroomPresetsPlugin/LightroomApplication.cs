namespace Loupedeck.LightroomPresetsPlugin
{
    using System;

    // Present because the SDK's plugin project template includes a
    // ClientApplication subclass, but intentionally left disconnected from
    // the host's "linked application" tracking - see the comment on
    // LightroomPresetsPlugin.HasNoApplication for why.
    public class LightroomApplication : ClientApplication
    {
        protected override String GetProcessName() => "";

        protected override String GetBundleName() => "";
    }
}
