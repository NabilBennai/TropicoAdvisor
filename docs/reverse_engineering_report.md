# Tropico 6 `.t6sav` — reverse-engineering report

What is known about the save format, how each piece of data was decoded, and how far it can be trusted.
The reference implementation is the C# code in `src/Tropico.SaveParser`; `tools/t6sav_decode.py` is the original Python prototype
(same format, kept for exploration and for cross-checking). Saves are only ever opened read-only.

Reference saves used for every number below (game build `t6-#1290-win64-steam@dcffff2`):

| | `urss Oct, 1934` (urss) | `Isla cuadrada Oct, 2069` (Isla) |
|---|---|---|
| File size | 1,716,103 B | 5,464,054 B |
| zlib stream offset | `0xDB` (219) | `0xD4` (212) |
| Decompressed size | 10,431,425 B | 57,331,094 B |
| Names / objects | 1,192 / 11,023 | 1,613 / 52,552 |
| Blob base | `0x9FDAF` | `0x2DBB53` |

Legend: **[V]** verified against an independent value in the save, **[P]** probable (consistent, not independently checked),
**[U]** unknown or only assumed. Anything below marked [U] is shown as uncertain in the application.

## 1. Container [V]

```
0x00  "Lama"                       signature
0x04  u32 (0x1b)                   not used
0x08  build string, NUL padded     up to 0x38     "t6-#1290-win64-steam@dcffff2"
0x38  u32 headerLength - 8         zlib offset = value + 8   (0xD3 -> 0xDB, 0xCC -> 0xD4)
...   length-prefixed strings      map id ("#RMG#RMG_Map#..."), save name, ..., Steam id ("Steam_...")
zlib  one zlib stream (78 9C) up to EOF
```

The zlib offset is derived from the header. The reader falls back to scanning for `78 01`, `78 5E`, `78 9C`, `78 DA` and keeps the first
candidate that inflates cleanly (only one does: the other hits are random bytes inside the deflate data). Exactly one stream exists.
The metadata strings are `i32 length (with NUL) + ASCII`; they are found by scanning the header, so the numeric fields between them
(timestamps, flags) are not decoded.

## 2. Decompressed layout [V]

```
[u32 N][N x (i32 length incl. NUL, ASCII, NUL)]     name table           ends 0x6B5C (urss)
[u32 M][M x object record]                          object table         ends = blob base
[blobs ...]                                         one blob per object, contiguous, sorted by offset
```

### Object record

```
u8 kind (1|2|3) | i32 length | path\0 | tail
tail = 0 bytes                      kind 3
     | u8 flag, u32 blobOffset      5 bytes
     | u8 flag, u32 blobOffset, i32 ownerIndex   9 bytes
```

The tail length is not stored. It is resolved by trying 0, 5 then 9 bytes and keeping the first that lands on a plausible next record
header (`kind` 1-3, sane length, printable path, NUL). The last record has no successor: 9 bytes if its flag byte is 1 or 4, else 5
(observed on two saves). This resynchronisation is the only heuristic in the table parser; all 11,023 + 52,552 records parse to the end.

* **kind 3** — class or data-asset reference (`#/Game/.../DA_T6BunkhouseBP_C`), **no blob**. Never an instance.
* **flag 2** — placed actor blueprint (buildings, deposits, managers of the world). **flag 1** — component or sub-object. **flag 4/8** — singleton managers.
* **ownerIndex** (9-byte tails) is the object-table index of the owner: components point to their building, `T6Stock` to its component,
  `T6AgentBehavior` to its agent. [V] (297 buildings = 297 `T6ConstructionSiteComponent`).
* **blobOffset** is relative to the blob base; a blob ends where the next distinct offset starts.

### Instance versus reference versus technical object

A **building instance** is a record under `/Game/Blueprints/Buildings/`, flag 2, not kind 3, owning at least one component. Ships
(`BP_T6Freighter`, 31 in urss) and `*Visualization*` helpers share the path prefix but are not buildings. Counting occurrences of a
Blueprint path in the data over-counts (class references, ships, helpers) and must not be used as an instance count.

## 3. Blob format: Unreal-style tagged properties [V]

```
[24-byte preamble for placed actors: f32 x, f32 y, f32 z, u32, f32 yaw?, u32]
repeat:
  FName name   (u32 nameIndex, u32 number)
  FName type   (u32, u32)
  u32 size
  u32 arrayIndex                              fixed C-array element (ServerCurrentNeedStatus[1..5])
  extra FNames                                Array/Struct/Enum/Set/Byte: 1, Map: 2
  u8 value                                    BoolProperty only
  u8 guid flag
  value[size]
FName "None" (index 0) ends a section; another section may follow.
```

* `ObjectProperty` = i32 object-table index. `EnumProperty` = FName (`ET6BuildingState::Built`). Arrays of scalar types = `u32 count` + items.
* **Array of structs**: `u32 count`, one header tag (name, `StructProperty`, size, index, struct-name FName, 16-byte GUID, flag),
  then `count` None-terminated property lists.
* A nested struct starts with one or two `None` FNames and has no terminator.
* **Only non-default values are serialized.** A missing `LifeState`, `BudgetLevel`, `CurrentSize` or `bIsImport` means the default
  (child, none, 0, false), not "unknown".
* Placed actors carry the 24-byte transform preamble (x, y, z verified against map positions; **yaw [U]**).

Parse coverage:

| | complete | resynchronised | partial | no blob (kind 3) | failed |
|---|---|---|---|---|---|
| urss (11,023) | 8,683 | 465 | 1,392 | 483 | 0 |
| Isla (52,552) | 41,528 | 2,055 | 7,513 | 1,448 | 8 |

* **Resynchronised** blobs contain undecodable native bytes between tags; the parser skips to the next plausible tag chain (heuristic, last resort).
* **Partial** blobs (mostly agents) are decoded up to the first native, non-tagged segment (~1.1 KB per agent). Agent properties before it
  (life state, education, happiness, thoughts) are reliable; what follows is not decoded.
* The 8 failing objects on Isla have not been investigated; the building, statistics and politics readers still match the reference values on that save.

## 4. Where the data lives and how far it is verified

### Statistics: `T6DataCollector` (one object, urss #7791)

History series are `T6HistoricalData` structs: `Entries[{ValueX, ValuesY[]}]`.

* **ValueX is the game month index** [V]: `TotalNumDays / 30` from `T6Calendar` equals the X of the last sample (417 for Sep 1934), so a
  61-sample history spans Sep 1929 to Sep 1934.
* **`ValuesY` omits trailing zeros** [V]: `[45, 3]` means `[45, 3, 0]`. Every list is padded to its fixed length before use.
* Category happiness histories store their value **at the slot of their own category** [V] (Food 0, Health 1, Job 2, House 3, Faith 4, Fun 5,
  Liberty 6, Safety 7; the only non-zero slot).

| Field | Status | Evidence |
|---|---|---|
| Citizen counters (`TotalCitizensNum`, adults, children, retired, prisoners, ...) | [V] | children / retired match agent objects; adults within 1 |
| Education distribution `[uneducated, high school, college]` | [V] | `[851, 454, 17]` vs living agents `[852, 454, 17]` |
| Age groups `[children, adults, retired]` | [V] | `[273, 952, 1]` equals the counters; toddlers are not included |
| Overall happiness (`OverallHappinessHistory[0]`) | [V] | 40.38 vs 40.37 mean of the agents' overall happiness (`CurrentHappiness[8]`, non-toddlers) |
| Happiness levels `[unused, low, medium, high]` | [V] low/medium, [U] high | 550 / 676 vs agents 553 / 673 |
| Citizens looking for a home | [P] | 127 adults with the thought `GoingToFindHome` for 48 homeless families |
| Unemployed `[59, 72, 1]` by education | [U] | same 3-slot layout as education; not checked against the game |
| Homeless families / vacant homes by 3 housing tiers | [U] | tier meaning assumed (cheapest first) |
| 5 wealth classes | [U] | sum equals the population; order assumed lowest first |
| Open jobs, workers history | [U] | names only |

### Economy

* `RevenueLastYear` / `ExpenseLastYear` are arrays of 12 monthly structs (exports by resource, fees, rents, imports, wages, upkeep).
  Sums are **[U]**: revenue minus expenses (129,309) does not reconcile with the sum of `MonthBalance` (141,536).
* Money is attributed to a **building class** (`Default__BP_T6Ranch_C` objects), never to an instance. Producers have no direct income
  (exports are not attributed to them): a negative class net is a **cost centre, not a loss**.
* `T6Stock` objects (owner = component = building) give resource, `StockType` (`InStock` / `OutStock`), `Capacity`, `CurrentSize`
  (missing = 0), reserved incoming/outgoing quantities. Summing them per resource matches `LastMonthResourceTotalAmounts` only roughly.
* `BP_T6GoodType_C` (56 goods): `PriceHistory` (48 samples), import/export volumes (12). **Sample order is assumed oldest to newest [U].**
* `T6TradeRoute` (114 offers, missing `bIsImport` = export) and `T6ActiveTradeRoute` (24 contracts). In the saves every contract has ended.

### Buildings and production

* `CurrentWorkmode` resolves to a workmode object (`BP_T6WorkmodeProfitProtocol_C`); `BudgetLevel` is present on workplaces (`Level5` for 139 of
  141 in urss). **The meaning of the levels [U].**
* An output stock at 90 % or more of its capacity is reported as "full" (a fact from the data; that production stops is game knowledge [P]).
* "Input stock empty" is **not** a usable starvation signal: shops and alternative-recipe factories are routinely empty.
* **Deposits** (`BP_T6*DepositMedium_C`, 74 in urss): position, `Radius`, `RemainingResourceAmount`. Amounts equal the initial values
  even under running mines, so they do not measure depletion. The radius unit is [U] (2-4 against map distances of thousands). Each mine's
  nearest deposit of its own resource is always the deposit it sits on; "deposits in use" is therefore **estimated by distance** [U].

### Population details, factions and politics

* **Factions** (10 `BP_Faction_*` objects) hold `ActiveModifiers`. Each modifier has a source: the faction's own history, a decaying
  `T6StandingModifierFleeting` event (source = the building), a `T6StandingModifierConstant` (source = an edict, target = a faction), or a
  reward/penalty from a demand. The history modifier value equals `HistoryAccountNet / 25` [V] (e.g. -194 / 25 = -7.76).
  Leader and corruption modifiers have **no stored value** [U]; the absolute scale of standing is [U]. Only sums and signs are used.
* **Edicts** (`BP_T6EdictManager_C`: a slot array where an empty slot is object index -1, plus custom edicts) with `bIsActive` and `MonthsActive`. **Constitution**: only topics with a
  non-default `ActiveOption` are stored (2 of 12 in urss). **Elections**: `MonthsUntilNextElections`. **Player state**: era, constitution
  date, research and victory points, built landmarks. **Demands**: `QuestStatus` (Rewarded, Abandoned, ...).

## 5. Not decoded

* Workers per building, worker capacity, housing capacity, residents, homes occupied / total, total jobs, homeless citizens (only the
  agent-thought estimate), electricity production and consumption, building "active" flag, agent to home / workplace links.
* **Per-resource indexed arrays** (`ResourceProduction*`, `LastMonthResourceTotalAmounts`, `ExportIncome`): they are indexed by the
  `ET6ResourceType` numeric value, which the save does not contain. The name table lists the enum names in first-use order (with `MAX`,
  `Unused1-4`), not in ordinal order. Matching values against stock totals was inconclusive (several candidates tie), so these arrays are not used.
* The native (non-tagged) segment of agent blobs and the "gaps" inside some building blobs, the `T6Money` struct, `Text` properties, actor yaw.
* Faction leader and corruption modifier values, constitution options at their default, superpower standings.

## 6. Reproducing and cross-checking

* C# (reference): `dotnet test`. The integration tests read two real saves from `samples/private/` (ignored by git) and pin the numbers above.
* Python prototype: `python tools/t6sav_export.py "<save>.t6sav" --out out` writes `decoded_raw.bin`, `strings.txt` (all strings with offsets),
  `object_table.csv`, `buildings.csv`, `building_classes.csv` (text occurrences vs records vs counted instances), `economy.json`,
  `population.json`, `island_summary.json` and `decode.log`; `--objects` also dumps every decoded property.
* `out/` holds the Python output for the two reference saves. It predates the C# readers: the C# code is authoritative where they differ.

## 7. Limits

* Two saves, one game build (`1290`). Other builds may reorder record tails or add properties; every parser step fails with an offset
  in the message instead of guessing.
* "Last sample of a history" can lag the live game by one sampling step.
* Thresholds used by the suggestions (unemployment 10 %, happiness 35 / 50, faction warning -10, treasury 3 / 24 months, wages 60 / 80 %)
  are heuristics of this project, not game rules.

## 8. Next steps

1. Compare with Almanac captures of the **same** save to settle the [U] items: unemployment order, housing tiers, wealth classes, budget levels,
   the scale of faction standing, price-history order.
2. Diff two saves of the same island (one building added, one family housed) to locate workers, residents and capacities.
3. Decode the native segment of agent blobs (home and workplace object indices are the likely content).
4. Establish the `ET6ResourceType` ordinals to unlock per-resource production and export history.
