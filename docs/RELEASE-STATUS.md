# 1.0.1-beta.1 — developer preview

Prepared locally; publication requires approval of this preview scope.

Includes:
- Source for the tray collector and native widget provider.
- Polish/English web widget, rounded corners and adaptive chart height.
- Per-user installer, privacy note, build notes and file hashes.
- Self-contained .NET 10 collector and rebuilt native widget provider.
- Widget behavior tests and MSIX manifest packaging validation passed.
- Store-identity MSIX prepared locally; it remains unsigned and unsubmitted.

Not verified:
- Clean-machine installation and runtime discovery.
- Store certification, signed installation, update/uninstall end-to-end.

Known constraints:
- Windows 11 x64; EEA web widget support; Developer Mode.
- Windows App Runtime 2 >= 2.4.0.0 must already be installed.
- Tray collector must run for fresh data.
- No automatic startup; no built-in dependency installer.

Do not attach local data or diagnostic files to public issues.
