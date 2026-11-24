# Area Calc — area table and plot limits — design

> Status: design, approved by Daniel (2026-11-23). Branch: `rishui-zamin`.
> Builds on [2026-11-22-area-usage-assign-design.md](2026-11-22-area-usage-assign-design.md)
> (usage groups, floor and sheet totals, `UsageGroups.Of`).

## 1. Intent

The רישוי זמין robot answers with a "חישוב שטחים בניין" table: one row per floor, one column
per usage under Primary / Service / Other, group totals, % of the plot, and above-ground /
below-ground / building totals. The office only sees it after uploading.

Daniel wants a similar table inside the Area Calc window **to check the numbers before the
upload**: to get an idea of the areas and see whether the building is **close to, or over,
what the plan (תב"ע) allows**. It does not have to match the robot to the decimal, nor look
like it.

## 2. Goals, non-goals, anti-goals

**Goals**
- A third tab, **Table**, next to Areas and Settings: floors as rows, used usage codes as
  columns grouped by Primary / Service / Other, group totals, % of plot, and above-ground /
  below-ground / building rows.
- The user types the **plot area** (m²) and the plan's **allowed % of plot** for Primary,
  Service and Total. A strip shows each used % against its limit, amber when close, red when
  over.
- Plot area and limits are kept **with the Revit project** (each project has its own plan).

**Non-goals**
- Matching the robot exactly, its proposed/existing split, or its layout.
- Limits in m², or separate above-/below-ground limits (Daniel: "nothing missing").
- Export, print or copy of the table.
- Areas on other sheets: the table covers the open sheet, like the rest of the window (a
  submission is one sheet).

**Anti-goals**
- A table whose totals disagree with the floor and sheet totals on the Areas tab. Both come
  from the same rule (`UsageGroups.Of`).
- Silently losing the typed plot area or limits.

## 3. Approaches

- **A. A tab in the Area Calc window (chosen).** The sums live in Core next to `UsageTotals`
  (tested without Revit), the tab renders them. It refreshes with the Areas tab.
- B. A Revit schedule placed on a sheet. Rejected: schedules cannot turn codes into columns
  per floor, and it means leaving the window.
- C. Export to Excel/HTML. Rejected: an extra step, and not what was asked.

For the limits, Daniel chose **limits as % of plot** over "just show % and compare by eye".

## 4. What the user sees

```
Plot area [ 2,370.00 ] m²   Allowed % of plot:  Primary [35 ]  Service [20 ]  Total [55 ]

 Primary 32.1% / 35%  ■ amber     Service 16.4% / 20%  ■ ok     Total 48.5% / 55%  ■ ok

 Elev.  Below  Floor        │ PRIMARY          │ SERVICE                        │ OTHER     │ No code │ Removed
                            │ 12        Σ    % │ 103    104    105    Σ       % │ 252   Σ   │         │ 300
 -4.90  yes    קומת מרתף    │                  │               24.40  24.40 1.0 │           │         │
  0.00         קומת קרקע    │ 1437.94  …       │ 208.79 11.04 230.12 756.33 …   │           │         │
  4.00         קומה א       │ 1037.79  …       │ …                              │ 496.09 …  │         │ 11.04
 ───────────────────────────────────────────────────────────────────────────────────────────────────────
 Above ground  …
 Below ground  …
 Building      …                                                     Total (Primary + Service): x m², y %
```

### 4.1 Inputs and the limits strip
- **Plot area** in m², and **Primary %**, **Service %**, **Total %**. Each box may be empty.
  Values are invariant-culture numbers; a box that does not parse is shown in error and not
  saved.
- **Used %** = group m² ÷ plot area × 100, taken from the **Building** row (above + below
  ground; Daniel, 2026-11-23). Basements count against the limits. Total = Primary + Service,
  the same Total as the floor and sheet totals.
- **Status** per limit: *ok* when used < 95 % of allowed; *close* (amber) when used ≥ 95 % of
  allowed and ≤ allowed; *over* (red) when used > allowed. Exactly at the limit is *close*,
  not over.
- An empty limit is not checked (its chip shows the used % only). With no plot area, every %
  cell shows "—" and the strip says "Type the plot area to see % of plot".

### 4.2 Columns
- One column per **effective code** in use on the sheet, in `UsageCatalog` order. The
  effective code follows `UsageGroups.Of`: the New code if it is in the catalog, else the Old
  code if it is, else none.
- Under **Primary** and **Service**: their codes, then Σ m², then % of plot. Under **Other**:
  its codes, then Σ. No % for Other (not limited, not in Total).
- **No code** (red header): areas with no effective code. Shown only when there are some, so
  the columns always add up to the sheet's m².
- **Removed**: the process codes 300–302 in use. They count in no group, no % and no limit.
- A group with no codes in use shows no code columns, only its Σ (zero) so the layout stays
  readable. My call; the builder may hide it instead.
- Column headers: code on one line, Hebrew name below, in **separate TextBlocks** (bidi
  constraint). Truncated names show in full in a tooltip.

### 4.3 Rows
- One row per floor (the Areas tab's level groups), ordered by **elevation ascending**, like
  the official table. Columns: elevation (m, two decimals), below-ground ("yes" or empty, from
  `AreaRecord.IsUnderground`), floor name.
- Then **Above ground**, **Below ground**, **Building**: sums of the floor rows. A row with
  nothing in it shows dashes. % columns in these rows are the same ratio to the plot.
- Cells: m² to two decimals (invariant, `F2`), empty when zero. % to one decimal.

### 4.4 Refresh
The table follows `AllRows`: it is rebuilt whenever the window reads the sheet (Refresh, and
the re-read after setting a usage). Typing a plot area or limit only recomputes the strip and
% cells; no re-read.

## 5. Storage of plot area and limits

- One Extensible Storage entity on the project's **Project Information** element, schema name
  `RVTukRishuiZaminPlotLimits`, vendor `KnafoKlimor`, a new fixed GUID. Four `double` fields:
  plot area m², primary %, service %, total %. **A negative value means empty.**
- **Read** on the Revit thread with the areas (the extract handler reads it alongside
  `FromOpenSheet` and hands it back with the result).
- **Write** when a box is committed (Enter or losing focus) and its value changed, through a
  new external event, in a transaction named "Area Calc — plot area and limits" (one undo
  step per commit).
- **Write fails** (read-only document, Project Information owned by someone else in a
  workshared model, the document is no longer the one the window read): the typed value
  stays in the window for this session, and the status line says it was not saved to the
  project and why. Same "is this still the document we read" guard as setting a usage.
- **Re-read vs unsaved values** (my call): a value that failed to save is kept, and the status
  line keeps saying it is not saved, for as long as the window reads the **same document**. A
  re-read of that document (Refresh, or the re-read after setting a usage) does not overwrite
  it. When the re-read finds a **different document**, its stored values replace everything
  typed, so one project's numbers are never shown against, or written into, another.
- KKarea (Revit 2023) gets the tab and the store: the store and its handler are linked into
  `KKarea.Revit.csproj` like the other Revit-side area sources. Extensible Storage exists in
  the 2023 API.

## 6. Where the code goes

- **Core** (`RVTuk.Core/RishuiZamin/`): the table model (columns, floor rows, summary rows,
  cell values), `PlotLimits` (the four optional values), the used-% and status rule, and the
  effective-code helper next to `UsageGroups.Of`. All xunit-tested on net48/net8/net10.
- **Revit** (`RVTuk.Revit/RishuiZamin/`): the Extensible Storage store, its write handler,
  and the read added to the extract handler. Registered in both `Application.cs` files.
- **UI** (`RVTuk.UI/RishuiZamin/`): the Table tab, the inputs and strip in the view model,
  columns built from the Core model. Colours reuse `Brush.UsagePrimary` / `UsageService` /
  `UsageOther` / `UsageProcess` and the existing error red; amber is `Brush.UsageProcess`'s
  yellow unless the theme has a warning brush.

## 7. Left to the builder

- Grid control (`DataGrid` with columns built in code, or a `Grid` built from the model), as
  long as the floor/elevation columns stay visible while scrolling sideways.
- The tab's header text and icon, matching the existing two.
- How the strip chips look, within the colours above.
