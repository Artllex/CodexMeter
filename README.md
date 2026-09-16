# Codex Meter Widget

A Windows 11 widget for Codex usage limits, with a local tray collector.

**1.0.1-beta.1 — developer preview.** Not a Microsoft Store release.
Unsigned; requires Developer Mode. Clean-machine installation is not yet verified.
The collector is self-contained on .NET 10 and does not require a separate .NET runtime.

## Features

- Remaining and used Codex limit, with reset time.
- Small, medium and large widgets.
- Large widget: line chart, axes, automatic 7d / 24h / 8h / 1h selection.
- Polish or English widget interface selected from the host language.
- Rounded corners, light/dark themes, local data processing.
- The companion tray application's interface is currently Polish.

## Requirements and installation

Windows 11 x64 (22H2 or newer), a current Windows Web Experience Pack,
Microsoft.WindowsAppRuntime.2 >= 2.4.0.0, and Codex installed and signed in (codex.exe on PATH or in
%LOCALAPPDATA%/OpenAI/Codex/bin). Microsoft documents web widgets as EEA-only.

1. Extract the release ZIP.
2. Enable Developer Mode in Windows Settings.
3. Run PowerShell in the extracted directory:
   powershell -NoProfile -ExecutionPolicy Bypass -File ./Install.ps1
4. Open Win+W and add Codex Meter.
5. Keep the collector running. Launch Codex Meter from Start after your next sign-in.

The installer checks prerequisites and refuses to overwrite an existing setup.
It installs per-user. It does not enable Developer Mode, import certificates,
install dependencies, add automatic startup or change the Codex login.

Removal: exit the tray app, remove the registered widget using
Get-AppxPackage Artllex.CodexMeterWidget | Remove-AppxPackage
then delete its versioned folder under LocalAppData/Programs and its Start shortcut.
Local usage history remains under LocalAppData/CodexMeter until you explicitly delete it.

## What the numbers mean

Limits refer to Codex in a ChatGPT subscription, not all ChatGPT conversations.
Charts aggregate local session token events, including repeated context and cached
input. They are not prompt length, cost, or a percentage conversion. Exact remaining
tokens are unavailable. Other devices are not included. Prompt ranking processes
local session text. See [Privacy](docs/PRIVACY.md).

Independent project, not affiliated with or endorsed by OpenAI or Microsoft.

## Development

Build collector: dotnet build src/Meter/CodexMeter.csproj -c Release
Provider: Visual Studio C++ tools, Windows SDK and NuGet restore for
src/Widget/SampleWidgetProviderApp.vcxproj.
Tests: node scripts/test-web-widget.cjs
See [Store preparation](docs/STORE.md).

Microsoft sample-derived code retains MIT notices in licenses/.
No new license for original project code is granted in this preview.
