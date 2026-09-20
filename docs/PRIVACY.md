# Privacy / Prywatność

Codex Meter reads account limits through the locally installed, signed-in Codex
app-server, which may contact OpenAI using the existing login. The application has
no independent analytics, advertising or developer-operated backend.

The collector reads local Codex sessions and archived sessions, processing prompt
text for optional ranking. It saves account usage snapshots, account identifiers
and token history under LocalAppData/CodexMeter/data.

The release contains no author account snapshots, credentials, chat transcripts,
private signing keys, diagnostic logs or local source-path configuration.
Never attach Meter/data, probe.json or session files to a public issue.
Removing the app does not erase collector history.

Polski: aplikacja korzysta z zalogowanego lokalnego Codex. Analizuje lokalne sesje,
w tym prompty dla rankingu, i zapisuje historię na komputerze. Nie ma własnej
telemetrii, reklam ani serwera zbierającego dane. Nie publikuj katalogu data,
probe.json ani sesji w zgłoszeniach błędów. Historię usuwa się osobno.
