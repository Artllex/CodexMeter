# UI components

The application uses the Windows Forms library already supplied by the Windows Desktop runtime. No additional UI package is required.

## Ownership

- `UI/CompletionPopup.cs`: completion details and expansion state.
- `UI/ChartHoverPopup.cs`: chart hover details.
- `UI/MeterForm.Selection.cs`: dropdown selection, search and repeat scrolling.
- `UI/MeterForm.History.cs`: compact history composition.
- `UI/MetadataTable.cs`: standard two-column TableLayoutPanel container.
- `UI/UiControls.cs`: shared labels and logical layout metrics.
- `UI/ActionButton.cs`: standard expansion button hover, pressed and alignment settings.
- `UI/ModelLine.cs`: colored reasoning value on one text baseline.
- `UI/ConversationLine.cs`, `UI/FlagLine.cs`, `UI/UsageChart.cs`: custom rendering where stock controls cannot express the required presentation.
- `UI/DarkMenuRenderer.cs`: standard ContextMenuStrip rendering.

## Layout rules

1. Dimensions passed to Controls, RowStyle and drawing APIs are physical pixels. Logical constants are scaled once at the view boundary.
2. Text measurements return physical pixels. Use UiMetrics.LogicalHeight only when returning a measurement to logical layout arithmetic.
3. Use UiControls.Text for metadata labels. The default is a zero-margin, vertically centered label. Expanded paragraphs may explicitly override alignment to TopLeft.
4. Both searchable and ordinary dropdowns use UiMetrics.SelectionRowHeight. Center text against the entire item rectangle, not the menu's intrinsic text rectangle.
5. ModelLine paints all model segments with one font and one baseline. Do not reintroduce separately sized labels for colored text.
6. Use ActionButton for expansion controls. Popup close buttons retain their distinct red destructive hover.
7. Preserve compact history and larger popup typography as deliberate view settings. Do not equate equal alignment with equal font sizes across views.

## Verification

Build the Release configuration and run the existing --selftest before deployment. Visually check ordinary and searchable dropdowns, wrapped flags, expanded prompt/JSON sections and the model line at the user's display scale. A successful build alone is not visual verification.

## History and rendering performance

History uses PromptHistoryCard with table rows inside a vertical FlowLayoutPanel. Flag rows measure their actual available content width. FlagLine shares its measurement algorithm with rendering and caches positions until the font, width or data changes. Cards own and dispose their fonts.

Run --ui-selftest to validate card row bounds and flag wrapping at four logical layout scales independently of taskbar integration. These fixture scales do not replace testing actual monitor DPI transitions.

Session analytics evicts cache entries for removed session files and uses a linear maximum selection for duplicate prompt records. No throughput benchmark is claimed.

ExpandableDetailSection owns the editor, toggle and height measurement for both prompt and JSON. The popup allocates the available screen height between those components.

UiTheme owns shared fonts for the application lifetime. Borrowed fonts must not be disposed by controls; explicitly owned fonts remain the responsibility of their card. The approved search field dimensions remain physical pixels by design.

SelectionMenuFactory isolates menus from MeterForm state; RepeatScrollController owns hold-to-scroll timing. MeterForm.Settings and MeterForm.Refresh isolate persistence and refresh orchestration. SessionParser handles JSONL parsing; Analytics aggregates cached results; SessionFileIndex caches the directory listing and invalidates it on file creation, deletion, rename and watcher errors, with a three-minute fallback scan.

The self-test validates the current one-hour, four-hour, one-day and seven-day chart ranges. OS taskbar integration is reported separately in taskbar-test.txt and is not silently marked as passed. The UI test exports each expanded popup section and checks restoration to the collapsed height.

scripts/Package.ps1 reads the version from the project, publishes a self-contained Windows x64 build, verifies its file version and creates a ZIP with its SHA-256. It refuses to overwrite existing output.
