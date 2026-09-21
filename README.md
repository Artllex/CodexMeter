# Codex Meter

[Polski](#polski) · [English](#english)

## Polski

Aplikacja Windows pokazująca użycie Codex w kompaktowym panelu, na pasku zadań i w obszarze powiadomień.

Interfejs dopasowuje się do języka wyświetlania Windows: po polsku na polskim Windows i po angielsku dla pozostałych języków systemu.

![Panel Codex Meter po angielsku](docs/images/codex-meter-panel-en-US.png)

### Funkcje

- Dynamiczny miernik procentowy na pasku zadań i w obszarze powiadomień.
- Pozostały i wykorzystany limit Codex wraz z czasem resetu.
- Lokalne wykresy użycia i historia ostatnich promptów.
- Powiadomienia o ukończeniu z liczbą tokenów wejściowych/wyjściowych, promptem, rozmową, modelem i poziomem myślenia.
- Polski i angielski interfejs oraz opcja menu zasobnika określająca, czy `X` minimalizuje okno do paska zadań, czy ukrywa je w obszarze powiadomień.

### Wymagania

Windows 11 x64 oraz zainstalowany i zalogowany Codex. Codex Meter odczytuje lokalne dane sesji Codex oraz API użycia app-server. Nie wymaga instalacji Widżetów Windows.

### Co oznaczają liczby

Limity dotyczą Codex w subskrypcji ChatGPT, a nie wszystkich rozmów ChatGPT. Wykresy agregują lokalne zdarzenia tokenowe sesji, w tym powtarzany kontekst i dane wejściowe z cache. Nie pokazują długości promptu, kosztu ani przeliczenia procentowego. Dokładna liczba pozostałych tokenów nie jest dostępna. Dane z innych urządzeń nie są uwzględniane. Zobacz [Prywatność / Privacy](docs/PRIVACY.md).

Niezależny projekt, niepowiązany z OpenAI ani Microsoftem i przez nie niezatwierdzony.

### Programowanie

Kompilacja: `dotnet build src/Meter/CodexMeter.csproj -c Release`

Kod wywodzący się z przykładu Microsoft zachowuje informacje MIT w `licenses/`.

---

## English

Windows application that displays Codex usage in a compact panel, on the taskbar, and in the notification area.

The interface follows the Windows display language: Polish for Polish Windows, English for every other system language.

![Codex Meter panel in English](docs/images/codex-meter-panel-en-US.png)

### Features

- Dynamic percentage meter on the taskbar and in the notification area.
- Remaining and used Codex limit with reset time.
- Local usage charts and recent prompt history.
- Completion notifications with input/output token counts, prompt, conversation, model, and reasoning level.
- Polish and English interface, plus a tray-menu option that controls whether `X` minimizes to the taskbar or hides the panel to the notification area.

### Requirements

Windows 11 x64 and Codex installed and signed in. Codex Meter reads local Codex session data and the Codex app-server usage API. It does not require a Windows Widgets installation.

### What the numbers mean

Limits refer to Codex in a ChatGPT subscription, not all ChatGPT conversations. Charts aggregate local session token events, including repeated context and cached input. They are not prompt length, cost, or a percentage conversion. Exact remaining tokens are unavailable. Other devices are not included. See [Privacy / Prywatność](docs/PRIVACY.md).

Independent project, not affiliated with or endorsed by OpenAI or Microsoft.

### Development

Build: `dotnet build src/Meter/CodexMeter.csproj -c Release`

Microsoft sample-derived code retains MIT notices in `licenses/`.
