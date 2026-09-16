# Microsoft Store preparation

Partner Center account verified. Product name reserved; the product is still a draft
and has not been submitted, approved, or published.

- Product: Codex Meter Widget
- Store ID: 9N2QVVT7VQDD
- Package identity name: Artllex.CodexMeterWidget
- Publisher: CN=74512847-387C-4F9E-9BCB-505BCA110D12
- Publisher display name: Artllex
- Package family name: Artllex.CodexMeterWidget_h63f2mvbs8j3t

Before Store release:
1. Replace sample screenshots with synthetic-data product screenshots (PL/EN).
2. Validate on a clean Windows 11 machine: dependencies, login, update, removal, sizes.
3. Run Windows App Certification Kit; complete privacy, age, markets, pricing and review notes.
4. Submit for certification. Submission is not approval or publication.

Completed locally: the collector uses self-contained .NET 10, stores shared data under
LocalAppData/CodexMeter, is included as a launchable application in the MSIX, and the
widget provider was rebuilt against the declared Windows App SDK dependency.

Proposed English listing:
Codex Meter Widget displays the remaining Codex limit, reset time and local token
activity in Windows Widgets. Three sizes; 7d, 24h, 8h and 1h chart views.
Requires Codex installed and signed in. Independent utility, not an OpenAI product.
Does not cover all ChatGPT conversations.

Proposed Polish listing:
Codex Meter Widget pokazuje pozostały limit Codex, termin resetu i wykres lokalnego
użycia tokenów w panelu Widżety Windows. Trzy rozmiary i zakresy 7d, 24h, 8h, 1h.
Wymaga zainstalowanego i zalogowanego Codex. Niezależne narzędzie, nie produkt OpenAI.
Nie obejmuje wszystkich rozmów ChatGPT.

EEA limitation:
https://learn.microsoft.com/en-us/windows/apps/develop/widgets/web-widget-providers
Package requirements:
https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements
