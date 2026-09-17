#!/bin/bash
# Double-click this file in Finder to build the plugin. It just runs the
# same "dotnet build" command from README.md for you, with plain-English
# messages instead of raw build output where possible.

cd "$(dirname "$0")"

echo "=========================================="
echo " Lightroom Presets - MX Creative Console"
echo " Build script"
echo "=========================================="
echo

if ! command -v dotnet >/dev/null 2>&1; then
	echo "The .NET SDK does not appear to be installed on this Mac yet."
	echo
	echo "To install it:"
	echo "  1. Open https://dotnet.microsoft.com/download/dotnet/10.0 in your browser"
	echo "  2. Download and run the macOS installer for the '.NET 10.0 SDK'"
	echo "     (pick Arm64 for Apple Silicon Macs, x64 for Intel Macs)"
	echo "  3. Come back and double-click this file again"
	echo
	read -p "Press Enter to close this window..."
	exit 1
fi

echo "Found .NET SDK version: $(dotnet --version)"
echo

if [ ! -d "/Applications/Utilities/LogiPluginService.app" ]; then
	echo "Note: couldn't find Logi Options+ / the Logi Plugin Service in its usual"
	echo "location. If it's installed somewhere else this is harmless, but if you"
	echo "haven't installed Logi Options+ yet (and opened it at least once), the"
	echo "build below will fail - install it from Logitech's website first."
	echo
fi

echo "Building the plugin - this can take a minute the first time..."
echo

dotnet build
BUILD_RESULT=$?

echo
echo "=========================================="
if [ $BUILD_RESULT -eq 0 ]; then
	echo "Build succeeded."
	echo
	echo "Open Logi Options+, go to the device configuration screen, and look"
	echo "under 'All Actions' for 'Apply Lightroom Preset' and 'Refresh"
	echo "Lightroom Presets'. If they're not there yet, quit and reopen"
	echo "Logi Options+ once."
else
	echo "Build FAILED - see the messages above for the reason."
	echo
	echo "This exact project has been verified to build clean against a real"
	echo "Logi Plugin Service install, so a failure here most likely means your"
	echo "installed version differs from that one (e.g. a different .NET"
	echo "version - check the error for a framework-version mismatch). Copy"
	echo "the full error text and send it back for a fix - see"
	echo "docs/TROUBLESHOOTING.md for more detail."
fi
echo "=========================================="
echo
read -p "Press Enter to close this window..."
