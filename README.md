# Tropico Advisor

Companion application for Tropico 6. It reads your save files (read-only) and shows an Almanac-like overview of the island together
with suggestions about what is going wrong or could be improved, each with the figures it is based on and how far the data can be trusted.

## What it shows

The desktop application lists the saves of `Documents\My Games\Tropico6\Saved\SaveGames` (the profile file is ignored), analyses the most
recent one and has six tabs:

| Tab | Content |
|---|---|
| Overview | Buildings, citizens, treasury and all suggestions |
| Buildings | Buildings by class (condition, workmode, budget level), production by resource (output storage, full producers), resource deposits |
| Population | Citizens, unemployment, homeless families, happiness by category, education, age groups, housing by tier, history charts |
| Economy | Yearly revenue and expenses, treasury and monthly charts, revenue / expense breakdown, costliest building classes |
| Trade | Price, stock, exports and imports per resource, trade routes |
| Politics | Faction standing with its causes (edicts, buildings, demands), active edicts, constitution, elections, demands |

The interface uses one fixed Tropico-inspired theme (lagoon teal, sand cream, gold and coral, serif titles); it does not follow the system
light / dark setting. Colors and styles live in `src/Tropico.Desktop/Themes/TropicoTheme.axaml`.

### Languages

English, French, Spanish, Italian and Arabic. The application starts in the language you last picked (language selector in the header),
otherwise in your system language when it is supported, otherwise in English. Arabic switches the whole window to right-to-left; the
history charts keep their left-to-right time axis and numbers keep Latin digits in every language.

* Translations are JSON catalogs in `src/Tropico.Localization/Resources/` (`en.json` is the reference; a test checks that every language has
  exactly the same keys and placeholders, and that long sentences are really translated).
* Suggestions are stored as catalog keys plus arguments (`LocalizedText`) and rendered in the chosen language, so switching language
  re-renders the current analysis without reading the save again.
* Game vocabulary that has a stable meaning (resources, factions, happiness categories, education levels, finance categories, eras) is
  translated. Building, edict, workmode and constitution names come from the game's internal identifiers and stay as the game names them.
* `TROPICO_LANGUAGE=fr|en|es|it|ar` overrides the saved choice for one run. The choice is stored in `%APPDATA%\TropicoAdvisor\settings.json`.
* The console tool (`Tropico.Cli`) prints in English.

### Suggestions and confidence

Every suggestion (`Finding`) has a category, a severity (`Info`, `Warning`, `Critical`), the evidence it relies on, a general lead and a
**confidence level**:

* `Verified` — read directly from the save.
* `Probable` — consistent with the save but not independently checked.
* `Uncertain` — depends on an assumption (for example the meaning of a series); shown dimmed in the interface.

Suggestions are leads to check, not orders: thresholds (unemployment, happiness, faction standing...) are this project's heuristics, not
game rules. What is known and what is still assumed about the save format is documented in
[`docs/reverse_engineering_report.md`](docs/reverse_engineering_report.md).

## Projects

- `Tropico.SaveParser` - Tropico 6 save file parser: container, name table, object table, tagged properties, and typed readers
  (buildings, statistics, trade economy, deposits, agents, politics)
- `Tropico.Analysis` - Island snapshot and rule engine that produces the suggestions
- `Tropico.Localization` - Languages, translation catalogs and localizable texts
- `Tropico.Data` - Local historical data storage (not implemented yet)
- `Tropico.Cli` - Prints a save summary and the suggestions in the console
- `Tropico.Desktop` - Avalonia desktop application

```
.t6sav --> Tropico.SaveParser --> Tropico.Analysis --> Tropico.Desktop / Tropico.Cli
           (read-only decoding)    (snapshot + rules)    (tabs, charts, console)
```

`tools/` holds the original Python prototype of the decoder (see below); `docs/` holds the format documentation.

## Development

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Tropico.Cli       # console summary of the latest save
dotnet run --project src/Tropico.Desktop   # desktop application
```

### Sample saves for the tests

The integration tests read two real saves and pin the decoded values. Saves are never committed (`*.t6sav` and `samples/private/` are
ignored). Copy them from your save directory into `samples/private/`:

* `Trop6_Sav_urss Oct, 1934.t6sav`
* `Trop6_Sav_Isla cuadrada Oct, 2069.t6sav`

Without them these tests fail with a message naming the missing file; the tests that use synthetic data do not need them.

### Python prototype

`tools/t6sav_export.py` decodes a save and writes dumps (decoded stream, strings with offsets, object table, buildings, economy, population)
into `out/`, which is useful to explore the format:

```powershell
python tools/t6sav_export.py "<save>.t6sav" --out out
```

The C# code is the reference implementation; `out/` was produced by the prototype before it and may differ.

## Notes

* Windows Smart App Control can block freshly built, unsigned test or application binaries (`0x800711C7`). Running the tests from an
  IDE, or turning that feature off, is a machine setting, not a project one.
* Not decoded yet: workers, residents, housing capacity and electricity. See the "Not decoded" and "Next steps" sections of the report.
