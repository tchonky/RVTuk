# Topo Edit Follows the Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** During a Topo Edit session the Topo Edit panel (✗ above ✓ at its left end) also shows on Revit's Modify tab, saving asks first, lines stay ordinary after leaving the session's view, heights are metres from the Survey Point in every project, and Floor ⇄ Topo leaves the Modify tab.

**Architecture:** Three pure pieces go to `RVTuk.Core/TopoTools` with xunit tests on net48, net8 and net10: the metre height text (`SurveyHeightText`), the summary's new sentences (`TopoSessionReport`), and which of our panels belong on the Modify tab (`ModifyTabPanels`). On the Revit side, `TopoEditPanel` builds the ✗/✓ stack. One host (`ModifyTabHost`, replacing `ContextualTopoButton`) puts our panels on the Modify tab as Revit's own panel class, made by reflection; the session's copy shares the real panel's `RibbonPanelSource`. The session gains `Started`, `Armed` and `DontAskOnSave`; its document events move to `TopoDocumentEvents`; `TopoSaveGuard` asks before a save; Accept and Cancel become callable (`TopoSessionActions`). A throwaway probe build, run once by Daniel in Revit 2024, settles the open points before the ribbon tasks.

**Tech Stack:** C#; Revit API 2024–2027 (Nice3point packages); Autodesk.Windows (compile-only, already referenced); UIFramework by reflection only; WPF imaging; xunit. net48 / net8 / net10.

**Spec:** [`docs/tools/topo-tools/specs/2026-11-17-topo-edit-follows-session-design.md`](../specs/2026-11-17-topo-edit-follows-session-design.md)

## Global Constraints

- Revit 2024–2027. Build each year: `dotnet build RVTuk.sln -c Release2024` (and `Release2025`, `Release2026`, `Release2027`) → 0 errors. Before every commit: `.\build\Test-All.ps1` → all pass. This binds commits on `topo-tools-finish`; the throwaway probe branch of Task 6 is exempt.
- Core stays free of Revit and WPF types. Namespace = folder. Core sources use block namespaces; tests use `namespace RVTuk.Core.Tests.TopoTools;`.
- Frozen identifiers (`tests\RVTuk.Core.Tests\Shared\FrozenIdentifierTests.cs`): `"TopoTools"`, `"FloorTopoConvert"`, `"TopoDrawLines"`, `"TopoHeightLabels"`, `"TopoUpdate"`, `"TopoAccept"`, `"TopoCancel"`, `"TopoSpacing"`, `PanelName = "Topo Edit"`, `LineStyleName = "_DP-Topo Line"`, `Name = "_DP-Topo Height"` and the updater GUID each stay in `src` exactly once. A second use goes through a constant.
- Panel and button names do not change. Command ids stay `CustomCtrl_%CustomCtrl_%Add-Ins%Topo Edit%<button>`. The panel's Autodesk.Windows source Id is `CustomCtrl_%Add-Ins%Topo Edit`.
- Tabs are found by Id only — `"Add-Ins"`, `"Modify"` — never by their localised title.
- Autodesk.Windows and UIFramework:
  - Every part catches everything, logs one line with `Application.LogDiagnostic`, and switches itself off for the rest of the Revit session.
  - Nothing of ours on the Modify tab is ever a plain `Autodesk.Windows.RibbonPanel`.
  - No Revit API call from a ribbon notification.
  - `Panels` is never changed inside its own notification: defer with the ribbon's `Dispatcher.BeginInvoke`.
  - Never write `Tag` or `CommandHandler` on API-made ribbon items.
  - No UIFramework reference in the project: the type `UIFramework.RvtRibbonPanel` is reached by reflection.
- Heights:
  - In metres, with `MetresPerFoot = 0.3048`.
  - Written as `Math.Round(m, 6, MidpointRounding.AwayFromZero)`, with −0 folded to 0, formatted `"0.00####"` with `CultureInfo.InvariantCulture`.
  - Read only within −500 to +3000 m, inclusive.
  - Internal Z = shared − `doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation`, unchanged.
  - Texts typed earlier in project units are not converted.
  - The Survey Point marker's figure: `r = Math.Round(m, 3, MidpointRounding.AwayFromZero)`, written `r.ToString("+0.00#;-0.00#", CultureInfo.InvariantCulture)`; no note when `r == 0`.
- Exact copy:
  - Summary: `"1 line skipped: '{text}' is not a height in metres."`; `"{n} lines skipped: {list} are not heights in metres."`; `"No topo line in the session's view lies over the toposolid or floor being edited."`; `"The Survey Point marker itself sits at {+10.00} m; heights are measured like a Survey Point level."`
  - Update tooltip: `"Apply the topo lines and their heights to the toposolid or floor being edited, and stay in the session"`.
  - Floor ⇄ Topo refusal: `"a Topo Edit session is open on it; Accept or Cancel it first"`.
  - Save prompt: command links `"Accept, then save"`, `"Cancel the session, then save"`, `"Save as it is"`; verification box `"Don't ask again until this Topo Edit ends"`.
  - Topo Tools during a session: `"Topo Edit is open on {label} in {view name}."` with command links `"Go to it"`, `"Accept it"`, `"Cancel it"`.
- Colours: RGB 255, 140, 0, set only on a newly created `_DP-Topo Line` and `_DP-Topo Height`.
- Icons: ✗ red and ✓ green on a transparent background, at 16 and 32 px. Light theme: #D9312B and #2F9E44. Dark theme: #FF6666 and #62D987. Folded panels show `TopoIcons.TopoTools(32)`.
- The Modify-tab Topo Tools shortcut: exactly one element selected, a `Toposolid` whose `HostTopoId` is `ElementId.InvalidElementId`.
- Update's summary: after Update only when `TopoSessionReport.ShowAfterUpdate` is true; after Accept always.

## Review Focus

1. **Heights typed the way people type them**: with the Hebrew keyboard on, with an invisible direction mark that Hebrew text leaves, with a comma, or with "m". Each is read as the same number, or named in the summary; none is misread. *Tests in Task 1.*
2. **The session ending while its copy is on the Modify tab**: ✓, ✗, the document closing, or another document activated. The copy leaves at once, and no dead buttons stay. *Test in Task 4; checks in Task 10.*
3. **Revit's edit modes with our panels on the tab**: Edit Boundary with a sketch line selected, or Edit Group. No Revit error, and our panels come back afterwards. *Probe 4; checks in Tasks 9 and 10.*
4. **Saving another document or a read-only one, or saving while closing**: no prompt for a document the session is not on, and nothing lost on Esc. *Probe 8; checks in Task 12.*
5. **A theme switch or a narrow window during a session**: ✗ and ✓ stay legible in both themes on both panels; a folded panel shows the Topo Tools icon, and one click opens ✗ over ✓. *Probe 5; checks in Tasks 5 and 10.*

Tasks 1–5 come before the probe, which is cut after Task 5. Tasks 11, 13 and 14 can be built while the probe sitting is pending. Only probes 5 and 9 can change any of these (probe 5 Task 5's stack and icons, probe 9 Tasks 2 and 3); if either does, Task 7 Step 3 changes that code. After the sitting (2026-11-18): Task 7, then Tasks 17 and 18, then Tasks 8, 9, 10 and 12, then 15 and 16.

---

### Task 1: Core — heights in metres (`SurveyHeightText`)

**Files:**
- Create: `src/RVTuk.Core/TopoTools/SurveyHeightText.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/SurveyHeightTextTests.cs`

**Interfaces:**
- Produces: `public static class SurveyHeightText` in `RVTuk.Core.TopoTools`, with:
  - `public const double MetresPerFoot = 0.3048;`
  - `public const double LowestMetres = -500;`
  - `public const double HighestMetres = 3000;`
  - `public static string Format(double sharedFeet)`
  - `public static bool TryParse(string? text, out double sharedFeet)`

- [ ] **Step 1: Write the failing tests**

```csharp
private static double Feet(double metres) => metres / SurveyHeightText.MetresPerFoot;

[Theory]
[InlineData(42.7, "42.70")]
[InlineData(42.755, "42.755")]
[InlineData(0.0, "0.00")]
[InlineData(-3.2, "-3.20")]
[InlineData(-0.0000001, "0.00")]   // −0 is folded
[InlineData(42.7500004, "42.75")]  // float residue goes at 6 decimals
[InlineData(1234.5, "1234.50")]    // no grouping
[InlineData(42.123456, "42.123456")]  // up to six decimals
public void FormatWritesMetresWithAtLeastTwoDecimals(double metres, string expected)
    => Assert.Equal(expected, SurveyHeightText.Format(Feet(metres)));

[Theory]
[InlineData("42.75", 42.75)]
[InlineData("42,75", 42.75)]
[InlineData("+42.75", 42.75)]
[InlineData("-3.2", -3.2)]
[InlineData("\u22123.2", -3.2)]           // minus sign U+2212
[InlineData("42.75 m", 42.75)]
[InlineData("42.75M", 42.75)]
[InlineData("\u00A042.75\u00A0", 42.75)]  // non-breaking spaces
[InlineData("\u200F42.75", 42.75)]        // RLM
[InlineData("\u200E42.75\u200E", 42.75)]  // LRM wrap
[InlineData("\u206642.75\u2069", 42.75)]  // LRI … PDI
[InlineData("\u061C42.75", 42.75)]        // ALM
[InlineData("\u200B42.75\uFEFF", 42.75)]  // zero-width space, BOM
[InlineData("42.75 m\u200F", 42.75)]      // a mark after the "m": marks go before the "m" rule
[InlineData("\u200E-3.20", -3.2)]         // LRM before a negative height
[InlineData("-\u200E3.20", -3.2)]         // a mark inside the text
[InlineData("\u202B42.75\u202C", 42.75)]  // RLE \u2026 PDF
[InlineData("1,050", 1.05)]               // a single comma is the decimal point
[InlineData("3000", 3000.0)]
[InlineData("-500", -500.0)]
public void TryParseReadsMetres(string text, double metres)
{
    Assert.True(SurveyHeightText.TryParse(text, out double feet));
    Assert.Equal(metres, feet * SurveyHeightText.MetresPerFoot, 9);
}

[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("   ")]
[InlineData("±0.00")]     // the building's own zero, typed out of habit
[InlineData("4275 cm")]
[InlineData("42750 mm")]
[InlineData("42.75 mm")]  // only the last 'm' is dropped: not 42.75 m
[InlineData("42.75mm")]
[InlineData("2750 mm")]   // millimetres inside the range: not 2750 m
[InlineData("=42+1")]
[InlineData("4 275")]
[InlineData("1,234.5")]
[InlineData("1.2.3")]
[InlineData("1e3")]
[InlineData("NaN")]
[InlineData("Infinity")]
[InlineData("42ץ75")]     // typed with the Hebrew keyboard on
[InlineData("42.7x")]
[InlineData("3000.01")]
[InlineData("-500.01")]
[InlineData("4275")]      // a forgotten point on a site above 30 m
[InlineData("+-3")]
[InlineData("m")]
public void TryParseRefuses(string? text) => Assert.False(SurveyHeightText.TryParse(text, out _));

[Fact]
public void EveryWrittenHeightReadsBackToItself()
{
    for (double m = -500; m <= 3000; m += 0.137)
    {
        Assert.True(SurveyHeightText.TryParse(SurveyHeightText.Format(Feet(m)), out double feet));
        Assert.Equal(Math.Round(m, 6), feet * SurveyHeightText.MetresPerFoot, 6);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter SurveyHeightTextTests`
Expected: the build fails, because `SurveyHeightText` does not exist.

- [ ] **Step 3: Implement `SurveyHeightText` in `src/RVTuk.Core/TopoTools/SurveyHeightText.cs`**

`Format`: `m = Math.Round(sharedFeet * MetresPerFoot, 6, MidpointRounding.AwayFromZero)`; `if (m == 0) m = 0;`; return `m.ToString("0.00####", CultureInfo.InvariantCulture)`.

`TryParse` reads in this order (the order matters):
```
1. null → false.
2. Drop U+200E, U+200F, U+061C, U+202A–U+202E, U+2066–U+2069, U+200B and U+FEFF;
   map U+2212 → '-' and U+00A0 → ' '; Trim().
3. If the text ends in 'm' or 'M', drop that one character and Trim() again.
4. More than one ',' or '.' in total → false. Then Replace(',', '.').
5. double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
   CultureInfo.InvariantCulture, out m); false on failure, on NaN and on ±Infinity.
6. m < LowestMetres or m > HighestMetres → false. Otherwise sharedFeet = m / MetresPerFoot.
```

- [ ] **Step 4: Run the tests to see them pass**

Run: the same filter → PASS. Then `.\build\Test-All.ps1` → all pass.

- [ ] **Step 5: Commit**

`feat(topo-tools): heights read and written in metres, in Core`

---

### Task 2: Core — the summary's new sentences

**Files:**
- Modify: `src/RVTuk.Core/TopoTools/TopoSessionReport.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/TopoSessionReportTests.cs`

**Interfaces:**
- Produces:
  - `NothingOverTarget = "No topo line in the session's view lies over the toposolid or floor being edited."`
  - `public double? SurveyPointMarkerMetres { get; set; }`. The report decides the note: only when the value rounded to the millimetre is not 0. Null, or a value that rounds to 0 (−0 included), means no note. The note is the last sentence. It is not a problem, so `HasProblems` does not change.

- [ ] **Step 1: Write the failing tests.** Change the asserted text at lines 25, 43, 51 and 110:
  - L25: `"2 lines skipped: '42,7x', '' are not heights in metres."`
  - L43: `"1 line skipped: 'abc' is not a height in metres."`
  - L51: `"No topo line in the session's view lies over the toposolid or floor being edited."`
  - L110: `"1 line skipped: 'x' is not a height in metres."`

  Then add:

```csharp
private const string Marker =
    "The Survey Point marker itself sits at +10.00 m; heights are measured like a Survey Point level.";

[Fact]
public void MarkerNoteComesLastAndIsNoProblem()
{
    var report = new TopoSessionReport { LinesApplied = 1, PointsAdded = 2, SurveyPointMarkerMetres = 10 };
    Assert.Equal("1 line applied: 2 points added, 0 replaced. " + Marker, report.Summarise());
    Assert.False(report.HasProblems);
}

[Fact]
public void MarkerNoteForAMarkerBelowZero()
{
    var report = new TopoSessionReport { LinesApplied = 1, SurveyPointMarkerMetres = -3.2 };
    Assert.Contains("The Survey Point marker itself sits at -3.20 m;", report.Summarise());
}

[Fact]
public void NothingOverTheTargetKeepsTheMarkerNote()
{
    var report = new TopoSessionReport { SurveyPointMarkerMetres = 10 };
    Assert.Equal(TopoSessionReport.NothingOverTarget + " " + Marker, report.Summarise());
}

[Fact]
public void MarkerNoteForAMarkerAFewMillimetresOff()
{
    Assert.Contains("The Survey Point marker itself sits at -0.003 m;",
                    new TopoSessionReport { LinesApplied = 1, SurveyPointMarkerMetres = -0.003 }.Summarise());
    Assert.Contains("The Survey Point marker itself sits at +0.003 m;",
                    new TopoSessionReport { LinesApplied = 1, SurveyPointMarkerMetres = 0.003 }.Summarise());
}

[Theory]
[InlineData(0.0004)]
[InlineData(-0.0004)]
[InlineData(-0.0000001)]
public void NoMarkerNoteWhenTheMarkerRoundsToZero(double metres)
{
    var applied = new TopoSessionReport { LinesApplied = 1, PointsAdded = 2, SurveyPointMarkerMetres = metres };
    Assert.Equal("1 line applied: 2 points added, 0 replaced.", applied.Summarise());
    Assert.Equal(TopoSessionReport.NothingOverTarget,
                 new TopoSessionReport { SurveyPointMarkerMetres = metres }.Summarise());
}

[Fact]
public void MarkerFigureIsNeverZeroOrDoubleSigned()
{
    for (int i = -200; i <= 200; i++)
    {
        var summary = new TopoSessionReport { LinesApplied = 1, SurveyPointMarkerMetres = i / 10000.0 }.Summarise();
        Assert.DoesNotContain("-+", summary);
        Assert.DoesNotContain("sits at +0.00 m", summary);
        Assert.DoesNotContain("sits at -0.00 m", summary);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter TopoSessionReportTests` → FAIL.

- [ ] **Step 3: Implement.** Change the two unreadable-height sentences and `NothingOverTarget`. `NothingOverTarget` is no longer returned on its own when a note follows it. Round the marker first, `r = Math.Round(m, 3, MidpointRounding.AwayFromZero)`: a note follows only when `r != 0`, and the same test decides whether `NothingOverTarget` stands alone. Write `r.ToString("+0.00#;-0.00#", CultureInfo.InvariantCulture)`, never `m`: two decimals would write a marker 3 mm off as +0.00, and net8 and net10 write a negative value that rounds to zero as "-+0.00". Fix the stale class comment: the summary is shown in the Update dialog, not "the pane's status line".

- [ ] **Step 4: Run the tests to see them pass**

Run: the same filter → PASS. Then `.\build\Test-All.ps1` → all pass.

- [ ] **Step 5: Commit**

`feat(topo-tools): the summary speaks of metres and the element being edited`

---

### Task 3: Revit — heights in metres, and the Survey Point marker note

**Files:**
- Modify:
  - `src/RVTuk.Revit/TopoTools/HeightTexts.cs:95-97`
  - `src/RVTuk.Revit/TopoTools/TopoSessionRunner.cs:44`
  - `src/RVTuk.Revit/TopoTools/TopoSessionUpdater.cs:1-3, 157`
  - `src/RVTuk.Revit/TopoTools/TopoSession.cs`
  - `src/RVTuk.Revit/TopoTools/Commands/TopoUpdateCommand.cs`
- Delete: `src/RVTuk.Revit/TopoTools/TopoHeightParser.cs`, `src/RVTuk.Revit/TopoTools/TopoLengthFormatter.cs`

**Interfaces:**
- Consumes: `SurveyHeightText.Format` and `TryParse` (Task 1); `TopoSessionReport.SurveyPointMarkerMetres` (Task 2).
- Produces: `public bool MarkerNoted { get; set; }` on `TopoSession`.

- [ ] **Step 1: Switch every height to metres**
  - `HeightTexts.CreateFor` writes `SurveyHeightText.Format(sharedFeet)`.
  - `TopoSessionRunner.Apply` and `TopoSessionUpdater.Execute` read with `SurveyHeightText.TryParse(text, out sharedFeet)`. The updater needs `using RVTuk.Core.TopoTools;`.
  - Delete the two Revit files. The conversion at `TopoSessionRunner.cs:30-31, 50` stays as it is.

- [ ] **Step 2: Add the marker note.** In `TopoUpdateCommand.Run`, after the report is built and before the dialog, when `!session.MarkerNoted`:
  - Read `BasePoint.GetSurveyPoint(doc).SharedPosition.Z`.
  - Set `report.SurveyPointMarkerMetres` to it in metres (`SharedPosition.Z * SurveyHeightText.MetresPerFoot`). The report leaves the note out when it rounds to 0 (Task 2); there is no threshold here.
  - Then set `session.MarkerNoted = true`.
  - A failure to read logs one line and leaves the note out. An Update that is rolled back does not set the flag.

- [ ] **Step 3: Build all four years; run `.\build\Test-All.ps1`** → 0 errors, all pass.

- [ ] **Step 4: Revit checks (README rows, Task 15):**
  - In a centimetre, a millimetre and a feet project, "42.75" puts points at 42.75 m.
  - New texts read 0.00, then the last typed height.
  - The accept and refuse lists of Task 1, typed into texts.
  - With the marker raised 10 m while unclipped, the first summary shown in the session (✓'s summary, or an Update that needs attention) names +10.00 m; a later summary in the same session does not.

- [ ] **Step 5: Commit**

`feat(topo-tools): heights are metres from the Survey Point in every project`

---

### Task 4: Core — which of our panels belong on the Modify tab (`ModifyTabPanels`)

**Files:**
- Create: `src/RVTuk.Core/TopoTools/ModifyTabPanels.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/ModifyTabPanelsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ModifyTabWanted(bool TopoEditCopy, bool TopoToolsShortcut);`. Core's `IsExternalInit` polyfill already covers net48.
  - `public static class ModifyTabPanels` with `public static ModifyTabWanted Decide(bool sessionOpen, bool inEditMode, bool oneTopoTargetSelected, bool copyAvailable)`.
- Rules, in order:
  1. In an edit mode, neither panel.
  2. Session open and copy available: the copy only.
  3. Otherwise: the shortcut, if and only if one toposolid or floor is selected.

- [ ] **Step 1: Write the failing test**

```csharp
[Theory]
[InlineData(false, false, false, true,  false, false)]
[InlineData(false, false, true,  true,  false, true )]
[InlineData(true,  false, true,  true,  true,  false)]
[InlineData(true,  false, false, true,  true,  false)]
[InlineData(true,  false, true,  false, false, true )]  // copy off: the shortcut stays, and opens the session dialog
[InlineData(true,  false, false, false, false, false)]  // copy off: the shortcut still follows the selection
[InlineData(true,  true,  true,  true,  false, false)]  // edit mode: nothing of ours
[InlineData(false, true,  true,  true,  false, false)]
public void Decides(bool session, bool editMode, bool oneTarget, bool copyAvailable, bool copy, bool shortcut)
    => Assert.Equal(new ModifyTabWanted(copy, shortcut),
                    ModifyTabPanels.Decide(session, editMode, oneTarget, copyAvailable));
```

- [ ] **Step 2: Run** `--filter ModifyTabPanelsTests` → FAIL (type missing).
- [ ] **Step 3: Implement** the three rules.
- [ ] **Step 4: Run** the filter → PASS. Then `.\build\Test-All.ps1` → all pass.
- [ ] **Step 5: Commit**

`feat(topo-tools): the rule for which panels go on the Modify tab`

---

### Task 5: Revit — the Topo Edit panel: ✗ above ✓ at its left end

**Files:**
- Modify: `src/RVTuk.Revit/TopoTools/TopoEditPanel.cs`, `src/RVTuk.Revit/TopoTools/TopoIcons.cs`

**Interfaces:**
- Produces:
  - `internal static BitmapSource TopoIcons.Cancel(int size, bool dark)` and `Accept(int size, bool dark)`: transparent background, colours from Global Constraints.
  - `internal static Autodesk.Windows.RibbonPanel? TopoEditPanel.FindOnAddIns()`: the Add-Ins panel whose `Source.Id == "CustomCtrl_%Add-Ins%Topo Edit"`, or null. Task 10 shares its `Source`.
- Rules:
  - The names go through constants, `CancelName = "TopoCancel"` and `AcceptName = "TopoAccept"`, so each literal still appears once.

- [ ] **Step 1: Build the panel in the new order.** In `Create`:
  - First `panel.AddStackedItems(cancelData, acceptData)`. Cancel is the top row, and the stack sits at the left end. Keep both as `PushButton` fields.
  - Then Draw Lines, Height Labels and Update, with Update's tooltip from Global Constraints.
  - Then Spacing, as today.
  - Choose the icons by `UIThemeManager.CurrentTheme == UITheme.Dark`. Subscribe `app.ThemeChanged`; on every event, re-read `CurrentTheme` and re-assign both buttons' `Image` (16 px) and `LargeImage` (32 px).

- [ ] **Step 2: Make the stack icons only, once, at the first `Show()`.** Do it in one method that never throws:
  1. Find the two Autodesk.Windows buttons on the "Add-Ins" tab by Id: `CustomCtrl_%CustomCtrl_%Add-Ins%Topo Edit%` + name. Hidden panels and stacks are searched too.
  2. Check before changing anything:
     - the panel's `Source.Id` is as above;
     - `Source.Items[0]` is a `RibbonRowPanel` holding exactly [cancel, `RibbonRowBreak`, accept];
     - both `LargeImage`s are set.
  3. On both buttons, set `Size = RibbonItemSize.Large` and `ShowText = false`. Leave `Orientation` horizontal, never vertical.
  - Any failure puts back `Size = Standard` and `ShowText = true`, logs one line, and stops trying for this Revit session.
  - Separately, in its own try, set the panel's `CollapsedPanelImage = TopoIcons.TopoTools(32)`.

- [ ] **Step 3: Build all four years; run `.\build\Test-All.ps1`.** FrozenIdentifierTests must pass.

- [ ] **Step 4: Revit checks (Task 15 rows):**
  - ✗ above ✓, 32 px, no text, at the left end. The Add-Ins panels are no taller during a session than before (two screenshots), and nothing on Topo Edit is cut off.
  - Tooltips titled "Cancel" and "Accept".
  - A light/dark switch during a session.
  - A folded panel shows the Topo Tools icon.

- [ ] **Step 5: Commit**

`feat(topo-tools): Cancel and Accept stacked at the left end of Topo Edit, icons only`

---

### Task 6: Probe — a throwaway build for one Revit 2024 sitting

Ten facts decide Tasks 8–12 (probe 5 may also change Task 5, and probe 9 Tasks 2 and 3): the spec's probe items 1–9, plus item 10 added here. Item 10 asks whether Draw Lines from another view, which sets the active view and then posts Detail Line, starts drawing in the session's view. They go into a throwaway build. Daniel runs it once in Revit 2024, and Task 7 records the answers. Nothing from this task is merged.

**Files (throwaway).** All on branch `topo-probe-2026-11-17`, in the worktree `D:\Coding\RVTuk-probe`, cut from `topo-tools-finish` after Task 5:
- Create: `src/RVTuk.Revit/TopoTools/TopoRibbonProbe.cs` (startup hooks and the log) and `src/RVTuk.Revit/TopoTools/TopoProbeCommand.cs` (an on-demand dump).
- Modify: `src/RVTuk.Revit/Application.cs`, with lines marked TEMPORARY: `TopoRibbonProbe.Install(application)`, and a "Topo Probe" button after Floor ⇄ Topo whose name comes from a constant.
- Modify: `src/RVTuk.Revit/TopoTools/TopoEditPanel.cs`, with lines marked TEMPORARY: `internal static bool SkipIconsOnlyLook`, checked first in Task 5's icons-only method, which then returns and leaves Size and ShowText as they are. The flag never reaches `topo-tools-finish`.

**Behaviour:**
- **The log** is `%TEMP%\RVTuk-topo-probe.txt`: append only, one timestamped line per event, and it never throws.
- **`TopoRibbonProbe.Install`** (OnStartup):
  - If `%TEMP%\RVTuk-probe-no-adwindows` exists, set `TopoEditPanel.SkipIconsOnlyLook = true` (probe 5's fail-safe look).
  - On Idling, notice `TopoSession.Current` turning non-null or null.
  - **Session start**, deferred with the ribbon's Dispatcher:
    - Make a `UIFramework.RvtRibbonPanel` by reflection, with `Source = TopoEditPanel.FindOnAddIns().Source` and `CollapsedPanelImage = TopoIcons.TopoTools(32)`, as Task 10 does. Add it to the tab with Id "Modify" and log its index (probes 1, 2).
    - Set the "Add-Ins" tab's `IsActive = true` and log the read-back (probe 3).
  - **Session end:** remove that panel.
  - **Watch the Modify tab:**
    - `Panels.CollectionChanged`: the action, and each panel's index, `Source.Id` and type name.
    - `PropertyChanged`: name and value, for Title, IsContextualTab, IsVisible and IsActive.
    - On every notification: the tab's `IsInEditMode`, read by reflection, and its `IsContextualTab` (probe 4); and the ribbon's active tab Id (probe 3).
    - When the tab's `IsActive` turns true, deferred with the ribbon's `Dispatcher.BeginInvoke(DispatcherPriority.Loaded)`: one line for each `Autodesk.Private.Windows.RibbonPanelControl` found under `ComponentManager.Ribbon` with `VisualTreeHelper`, giving `Panel.Tab?.Id`, `Panel.Source?.Id`, the panel's type name, `ActualHeight` and `IsCollapsedNow` (probe 5: the panels' own heights).
  - **`ViewActivated`:** Status, the document title, and the previous and current view names (probe 6).
  - **`DocumentSaving` and `DocumentSynchronizingWithCentral`:** a TaskDialog "Probe 8: press Esc to stop this save, or choose Go on". Esc calls `e.Cancel()` when `e.Cancellable`. Log Cancellable and the choice. Also log `DocumentClosing` with its DocumentId and Cancellable, and `DocumentClosed` with its DocumentId and Status (probe 8). `DocumentClosingEventArgs` is a pre-event and has no Status.
- **`TopoProbeCommand`** ("Topo Probe" on the RVTuk panel) shows one TaskDialog and logs the same text:
  - **Probe 9**, all in metres:
    - `GetProjectPosition(XYZ.Zero).Elevation`;
    - `BasePoint.GetSurveyPoint(doc).SharedPosition.Z` and `.Position.Z`;
    - the Survey Point's `BASEPOINT_ELEVATION_PARAM`;
    - `InternalOrigin.Get(doc).SharedPosition.Z`.
  - **Probe 7:** whether `UIFramework.ShortcutsHelper.Commands`, read by reflection, holds the ids of TopoCancel and TopoUpdate, with TopoTools as a control.
  - **Probe 4:** the Modify tab's panels (index, `Source.Id`, type, IsContextualTabThemeIgnored, IsVisible), and the tab's Title, IsContextualTab and IsInEditMode.
  - **Probe 5:** the same per-panel lines as when the Modify tab becomes active, and `ComponentManager.Ribbon.ActualHeight`.
  - **Probe 10:** with a session open and another view active, set `ActiveView` to the session's view, then call `CanPostCommand` and `PostCommand(PostableCommand.DetailLine)`. Log each result.

- [ ] **Step 1: Build the probe.** Create the worktree and branch, write the two files and the temporary lines, and build `-c Release2024` → 0 errors. Test-All is not run on this branch: it is never merged. Then commit the probe on `topo-probe-2026-11-17` in one commit, `chore(topo-tools): throwaway probe, never merged`. Commit any later change to the probe the same way, so the worktree stays clean.

- [ ] **Step 2: Ask Daniel for the sitting, and wait for the results.** Send him this script:

  > **Before you start:**
  > - Close Revit 2024.
  > - In an elevated PowerShell, in `D:\Coding\RVTuk-probe`, run `.\Deploy.ps1 2024`.
  > - Use a copy of a test project with a toposolid, a floor, a group and two plan views, and save it often: step 6 may show a Revit error.
  > - This build reads every height text in metres. In the copy, delete the topo lines and height texts in the plan view you will use, or retype their texts in metres: a text typed earlier in the project's units is refused (above 3000) or read as metres (below), and every Update and ✓ applies it.
  > - Before any session, open Add-Ins and press Topo Probe once (the panels' heights before).
  >
  > **The sitting:**
  > 1. Select the toposolid in a plan view. On Modify | Toposolid, press the Topo Tools button there (not the one on the RVTuk panel). Does the ribbon jump to Add-Ins? On Topo Edit: ✗ above ✓ at the left, icons only, nothing cut off? Then, without clicking any tab, press Esc. Does the ribbon stay on Add-Ins? Select a wall and press Esc. Which tab shows?
  > 2. Select the toposolid again. On Modify | Toposolid, is the Topo Edit copy there, last, in the tab's colours? Do the ✗ and ✓ tooltips read "Cancel" and "Accept"?
  > 3. On the copy, press Draw Lines and start a line. While still drawing, click ✓ on the copy. Does it stop drawing and accept?
  > 4. Start a session again. Type 50 into Spacing on the copy, then look at Spacing on Add-Ins. Is it one box?
  > 5. Start Detail Line from the Annotate tab and press Esc. Is the copy still there?
  > 6. With a session open: Edit Boundary of the floor, select a sketch line, then Finish. Any error? Still in the session: select the group, Edit Group, select an element in it, then Finish. Then Edit Boundary of the floor again, and Cancel. Any error? Then, with nothing selected, start a new floor (Architecture → Floor) and press Cancel Edit Mode without drawing; select nothing, and press Topo Probe. Then end the session, select the floor (the shortcut shows on its Modify tab), Edit Boundary, select a sketch line, then Finish. Any error? Write down its text.
  > 7. At your usual window width and display scaling, on Modify | Place Detail Lines and on Modify | Toposolid: is the copy whole, or folded into one button? Try "Minimize to Panel Buttons": does one click open ✗ over ✓? Which icon does the folded copy show? And the folded Topo Edit on Add-Ins?
  > 8. Switch Revit between light and dark during a session. Are both panels legible?
  > 9. Tile two views, click into the other one, then back.
  > 10. With a session open, press Topo Probe and copy what it shows (the panels' heights during a session). Then open another view and press Topo Probe again. Does Revit switch to the session's view and start Detail Line there?
  > 11. With unsaved changes and a session open, close the model. Answer Yes at "Save changes?", then press Esc at the probe prompt. Does the model stay open? On a workshared copy, do the same after choosing Synchronize.
  > 12. Heights. With the Survey Point clipped, use Specify Coordinates at Point at ±0.00, Elevation 42.75 m.
  >     - Draw a line, type 42.75 into its height text, and press Update. This build reads heights in metres in every project, whatever its units: 4275 or 42750 would be refused. The summary must name no skipped line.
  >     - Compare, at that spot: a Survey Point spot elevation; a level with Elevation Base = Survey Point; Modify Sub Elements with Elevation Base = Survey Point.
  >     - Then unclip the Survey Point, set its Elev to 10 m, and clip it again. Read all three again, and press Topo Probe.
  > 13. Create an empty file `%TEMP%\RVTuk-probe-no-adwindows`, restart Revit and start a session. Do you see a small "Cancel" / "Accept" stack with words, with nothing cut off? Press Topo Probe. Delete the file afterwards.
  > 14. Only if Revit 2026 or 2027 is at hand: deploy the probe for it, start a session as in step 1, and repeat steps 2, 5, 6 and 12.
  >
  > **Send back:** `%TEMP%\RVTuk-topo-probe.txt`, plus a line or a screenshot per step.

---

### Task 7: Record the probe and settle the open points

Done on 2026-11-18 with the decisions in the spec's 'Probe results (Revit 2024, 2026-11-18)'; the probe branch stays until Task 16.

**Files:**
- Modify: the spec (a new section, "## Probe results (Revit 2024, <date>)"), and this plan, amending the Global Constraints and Tasks 2, 3 and 8–12 where a result changes them.
- Only if rule 9 fires, also: `src/RVTuk.Revit/TopoTools/TopoSessionRunner.cs:28-31, 50`, `src/RVTuk.Revit/TopoTools/Commands/TopoUpdateCommand.cs`, `src/RVTuk.Revit/TopoTools/TopoSession.cs`, `src/RVTuk.Core/TopoTools/TopoSessionReport.cs`, `tests/RVTuk.Core.Tests/TopoTools/TopoSessionReportTests.cs`.
- Only if rule 5 changes the look, also: `src/RVTuk.Revit/TopoTools/TopoEditPanel.cs`, `src/RVTuk.Revit/TopoTools/TopoIcons.cs`.
- Only if rule 7 fires, also: Task 15 of this plan (its conditional README note).

- [ ] **Step 1: Record each item's answer in the spec.** Probe 9's line was typed 42.75 into the probe build, which already reads metres (Task 3); Task 3 left the step from shared to internal heights unchanged, so the answer holds for it.

- [ ] **Step 2: Decide by these rules, and write each decision into the spec:**
  - **1 fails** (wrong colours, dead clicks, two Spacing boxes, or ✓ while drawing does not accept): no copy on the Modify tab. `CopyAvailable` stays false; Task 10 keeps only the switch to Add-Ins; the shortcut stays during a session and opens Task 11's dialog.
  - **2:** if the copy drops off or moves during Revit's changes, Task 9's host puts it back on the tab's notifications. A position other than last is noted.
  - **3:** if Add-Ins does not come back after a selection and Esc, keep the one switch at the start and note it.
  - **4:**
    - An error with today's plain shortcut panel means the crash is real, and PR #16 must not merge before Task 9.
    - Task 9's edit-mode signal is `IsContextualTab && IsInEditMode` if that pair was true exactly during edit modes, Edit Boundary and Edit Group alike, and false again once each ended, after Finish and after Cancel, including the Topo Probe reading after step 6's cancelled new floor. `IsInEditMode` alone cannot be the signal: UIFramework starts it true and writes it only when it builds a contextual tab (`ContextualTab.setProperties`); tearing that tab down resets only `IsContextualTab`, so after an edit mode that ends with nothing selected the plain Modify tab still reads true (2024, 2026 and 2027). Otherwise use the signal the log shows, for example a Revit Mode panel's `Source.Id` on the tab.
  - **5:**
    - If the icons-only stack looks wrong on Add-Ins (the panels taller during a session than before, by the Topo Probe readings before the sitting and at step 10; anything on Topo Edit cut off; or text still shown, step 1) and step 13 shows the fail-safe stack whole: Step 3 ships the stack as the API builds it, small icons with "Cancel" and "Accept". TopoEditPanel no longer makes it Large with ShowText off at the first `Show()`; the `CollapsedPanelImage` keeps its own try; Task 5 Step 4's checks, and so Task 15's rows, change to that stack.
    - If the fail-safe stack is wrong too, the choice goes back to Daniel.
    - If step 8 shows ✗ or ✓ unreadable in one theme, Step 3 changes that theme's colours in the Global Constraints and in `TopoIcons`.
    - If the copy is folded at the usual width, the choice goes back to Daniel: accept it, or raise the copy's collapse priority.
    - If the look passes, Task 5's code stays as it is.
  - **6:** if a tiled view does not raise `ViewActivated`, note that such a switch does not disarm.
  - **7:** if Topo Edit's ids are missing from the list, add a keyboard-shortcut note to the README and the backlog idea (Task 15 Step 1 carries the note, Step 4 the idea).
  - **8:** if Esc during a close lets Revit close without saving, Task 12's Esc saves as it is in every case.
  - **9:** if Revit's readings follow the marker, the conversion becomes `internalZ = h + BasePoint.GetSurveyPoint(doc).Position.Z` and the marker note is dropped. Amend the Global Constraints (the Internal Z bullet becomes this formula, read again at every Update; the marker bullet and the marker sentence go), Tasks 2 and 3 (Task 3 Step 4's marker check becomes: with the marker raised 10 m while unclipped, Update raises the points 10 m, as a Survey Point level reads), and the spec. Then do Step 3.
  - **10:** if switching then posting does not start drawing in the session's view, Task 8's Draw Lines switches the view and says "Press Draw Lines again to draw in {view name}."

- [ ] **Step 3: Only if rule 5 or rule 9 fired, change the code already built.** On `topo-tools-finish`, each rule as its own commit, after building all four years → 0 errors and `.\build\Test-All.ps1` → all pass:
  - **Rule 9:**
    - `TopoSessionRunner.Apply`: replace lines 30-31 with `double markerZ = BasePoint.GetSurveyPoint(doc).Position.Z;` and line 50 with `double z = sharedFeet + markerZ;`; rewrite the comment at lines 28-29 to match.
    - `TopoSessionReport`: remove `SurveyPointMarkerMetres` and its sentence; `NothingOverTarget` is again returned on its own. In `TopoSessionReportTests`, remove the `Marker` constant and every test that sets `SurveyPointMarkerMetres`, but keep `MarkerNoteComesAfterEveryOtherSentence`'s sentence-order check without the marker, renamed for what it then checks; take the marker out of the class comment, keeping that `NothingOverTarget` stands in for the other sentences.
    - Remove `TopoSession.MarkerNoted`, and Task 3 Step 2's note code from `TopoUpdateCommand.Run`.
    - Commit `fix(topo-tools): heights measured from the Survey Point marker, as probe 9 found`.
  - **Rule 5:** change `TopoEditPanel` and `TopoIcons` as the rule says. Commit `fix(topo-tools): the Cancel and Accept stack as probe 5 found`.

- [ ] **Step 4: Delete the probe.**
  - From `D:\Coding\RVTuk`, run `git worktree remove D:\Coding\RVTuk-probe`, then `git branch -D topo-probe-2026-11-17` (`-D`: the probe commit is never merged). The worktree is clean: the probe is committed (Task 6 Step 1), and build and deploy output is ignored.
  - If the removal still refuses, run `git -C D:\Coding\RVTuk-probe status --porcelain`. If it lists only the probe's own files, commit them on the probe branch and retry; if it lists anything else, stop and ask Daniel.
  - Tell Daniel that the probe build is still installed in Revit 2024, and in 2026 or 2027 if step 14 put it there, with its save prompt. In an elevated shell with Revit closed, he replaces it with `.\Deploy.ps1 2024` from `D:\Coding\RVTuk`, adding 2026 or 2027 as needed.

- [ ] **Step 5: Commit**

`docs(topo-tools): record the follow-the-session probe`

---

### Task 8: Revit — the session: arming, a close called off, Draw Lines from another view

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/TopoDocumentEvents.cs`
- Modify:
  - `src/RVTuk.Revit/TopoTools/TopoSession.cs`
  - `src/RVTuk.Revit/TopoTools/TopoSessionUpdater.cs:107-131`
  - `src/RVTuk.Revit/TopoTools/LastTypedHeights.cs` (two new members, `KeyFor` and `ForgetKey`; `Forget` goes; the comment says DocumentClosed)
  - `src/RVTuk.Revit/TopoTools/Commands/DrawTopoLinesCommand.cs:20-34`
  - `src/RVTuk.Revit/Application.cs:299-325` (the two handlers move out; it calls `TopoDocumentEvents.Install(application)`)

**Interfaces:**
- Produces:
  - On `TopoSession`:
    - `public static event Action? Started;`, raised by `Start` after `Current` is set. Handler failures are logged as `Ended`'s are.
    - `public bool Armed { get; set; } = true;`
    - `public bool DontAskOnSave { get; set; }`
  - `public static Document? LastTypedHeights.KeyFor(Document doc)`: the stored key object that `Equals` doc, or null. Call it only while doc is open.
  - `public static void LastTypedHeights.ForgetKey(Document key)`: removes the entry whose key is `ReferenceEquals` to key, calling neither `Equals` nor `GetHashCode` on it. Keep the entries in a list (`Get` and `Set` find theirs with `Equals`), or rebuild the dictionary from the other entries.
  - `internal static class TopoDocumentEvents { internal static void Install(UIControlledApplication app); }`, covering DocumentClosing, DocumentClosed and ViewActivated.
- Rules:
  - **DocumentClosing:** remember, by `e.DocumentId`, whether the session is on the document, and `LastTypedHeights.KeyFor(e.Document)`. Leave the height where it is: a save on the way out can run Update (Task 12's "Accept, then save"), whose Height Labels pass gives each line without a text `LastTypedHeights.Get(doc)`. Never end the session here.
  - **DocumentClosed**, for the remembered id:
    - Status `Succeeded`: end the session if it was on that document, and call `LastTypedHeights.ForgetKey` on the remembered key when there is one.
    - Otherwise there is nothing to put back.
    - Never call the API, `Equals` or `GetHashCode` on the closed document. `Set` runs only when a text is retyped, which cannot happen between the two events, so no entry for the closing document appears after `KeyFor`.
  - **ViewActivated:**
    - Another document: end the session, as today.
    - The same document, with `e.Status == RevitAPIEventStatus.Succeeded` and `e.CurrentActiveView.Id != session.ViewId`: `Armed = false`.
  - **Updater:** for a new detail line, `bool alreadyTopo = TopoLineStyle.IsTopoLine(line)`.
    - Skip the line when `!session.Armed && !alreadyTopo`.
    - Restyle only when `!alreadyTopo`.
    - The rest stays as today.
  - **Draw Lines:**
    - Only when the active view of the session's document is not the session's view: set `uiDoc.ActiveView` to it. On failure, show the old "Go back to {view.Name} to draw topo lines." and return Cancelled.
    - In every case, pressed in the session's view or after a successful switch: once `CanPostCommand` passes, set `session.Armed = true`, then post Detail Line as today. The usual ways back to the session's view (its tab, a tiled view, "Go to it") do not arm; pressing Draw Lines does. A press that returns Cancelled does not arm.
    - Task 7 kept the switch-then-post (rule 10: not observed); the probe-10 fallback ("Press Draw Lines again…") comes only if Step 3's check fails. Arming stays as it is. Under that fallback, the press that only switches does not arm, and the second press, which posts, does.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build all four years; run `.\build\Test-All.ps1`.**
- [ ] **Step 3: Revit checks (Task 15 rows):**
  - A close called off at "Save changes?" keeps the session, and the next new line still takes the last typed height.
  - After visiting another view, a plain Detail Line drawn in the session's view stays ordinary; a copied topo line still gets its text; Draw Lines arms the session again.
  - Draw Lines pressed in another view opens the session's view and draws there.
- [ ] **Step 4: Commit**

`feat(topo-tools): the session survives a close called off; arming; Draw Lines from any view`

---

### Task 9: Revit — one host for our Modify-tab panels: the shortcut

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/ModifyTabAccess.cs`, `src/RVTuk.Revit/TopoTools/ModifyTabHost.cs`
- Delete: `src/RVTuk.Revit/TopoTools/ContextualTopoButton.cs`
- Modify:
  - `src/RVTuk.Revit/Application.cs`:
    - Line 285 becomes `ModifyTabHost.Install(application)`. The comment above it (282-284) says that `ModifyTabHost` puts our panels on the Modify tab, without Floor ⇄ Topo.
    - In the `<summary>` of `TopoToolsButtonName`, "(see ContextualTopoButton)" becomes `(see <see cref="ModifyTabHost"/>)`.
    - In the `<summary>` of `FloorTopoConvertButtonName`, drop ": the contextual Floor ⇄ Topo button posts it". Keep the constant: the RVTuk-panel button uses it.
    - Name these by constant, not line number: Task 8 shifts the lines after 299.
  - `src/RVTuk.Revit/TopoTools/Commands/TopoStartCommand.cs:15`: in the class summary, `<see cref="ContextualTopoButton"/>` becomes `<see cref="ModifyTabHost"/>`, on the same line, so Task 11's and Task 14's line numbers still hold.
  - `src/RVTuk.Core/TopoTools/ModifyTabPanels.cs`: the parameter `oneTopoTargetSelected` becomes `oneToposolidSelected`, its `<param>` "One toposolid, not a subdivision, is selected."; the class summary's "while one toposolid or floor is selected" becomes "while one toposolid is selected".
- Test: `tests/RVTuk.Core.Tests/TopoTools/ModifyTabPanelsTests.cs`: the named argument in `NamesTheCopyAndTheShortcut` follows the rename; nothing else changes there.

**Interfaces:**
- Consumes: `ModifyTabPanels.Decide` (Task 4); `TopoSession.Started` and `Ended` (Task 8); the edit-mode signal (Task 7: `IsContextualTab && IsInEditMode`, rule 4).
- Produces:
  - `internal static class ModifyTabAccess`, with:
    - `internal const string AddInsTabId = "Add-Ins";` and `internal const string ModifyTabId = "Modify";`
    - `internal static AwRibbonTab? FindTab(string id)`: `ComponentManager.Ribbon?.FindTab(id)`.
    - `internal static AwRibbonPanel NewRevitPanel(AwRibbonPanelSource source)`: the type `UIFramework.RvtRibbonPanel` from the loaded `UIFramework` assembly, made with `Activator.CreateInstance`, cast to `AwRibbonPanel`, with `Source = source`. It never returns null, never hands back a half-made panel, and logs nothing: the assembly not loaded, the type not found, or an instance that is not an `Autodesk.Windows.RibbonPanel` each throw `InvalidOperationException` naming that step and the type; exceptions from `CreateInstance` (a `TargetInvocationException` unwrapped to its inner exception) and from the `Source` setter propagate. The caller logs.
    - `internal static bool IsInEditMode(AwRibbonTab modifyTab)`: `modifyTab.IsContextualTab &&` the tab's `IsInEditMode`, read by reflection: the signal Task 7 kept (rule 4). It throws when the signal cannot be read; the caller's fail-safe handles that.
  - `internal static class ModifyTabHost { internal static void Install(UIControlledApplication app); internal static bool CopyAvailable { get; } }`. Task 10 adds the copy; here `CopyAvailable` is false. `Install` also reads two test switches once (Rules).
- Rules:
  - Floor ⇄ Topo's Modify-tab button is gone. The shortcut panel holds Topo Tools alone: Id `RVTuk_ContextualTopoTools`, posting the RVTuk-panel command as today.
  - **One idempotent `Apply()`.**
    - Inputs: the session open (`TopoSession.Current`); the edit mode (`IsInEditMode`); exactly one element selected, a toposolid that is not a subdivision (from the last `SelectionChanged`); `CopyAvailable`.
    - It calls `Decide`, then adds or removes each of our panels, never only hiding one.
    - Our panels come only from `NewRevitPanel`. If it throws while making the shortcut panel, nothing of ours goes on the tab: the fail-safe below runs.
  - **When `Apply` runs:**
    - On `SelectionChanged`, on `TopoSession.Started` and `Ended`, and on the Modify tab's `Panels.CollectionChanged` and `PropertyChanged`.
    - Always deferred through the ribbon's `Dispatcher.BeginInvoke`, with one call pending at a time.
    - Never inside the notification, and never calling the Revit API.
  - **Fail-safe**, as `ContextualTopoButton.Fail` today: log one line (the step, then the exception's type and message), remove our panels, unhook, and stop for the Revit session. It covers finding the Modify tab, the edit-mode signal, and making, adding and removing the shortcut; Task 10's copy has its own failure rule.
  - **Test switches, for checking the fail-safe in Revit.** `Install` looks once for two empty files in `%LOCALAPPDATA%\RVTuk\` (the folder of `crash.log`). Their names are constants in `ModifyTabHost`. The look never throws, and an error counts as no file.
    - `topo-modify-tab-off`: `Install` runs the fail-safe at once, with the step "switched off by topo-modify-tab-off". Nothing of ours goes on the Modify tab.
    - `topo-modify-copy-off`: `CopyAvailable` stays false for the Revit session (Task 10 checks it before making the copy). One line is logged, naming the file.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build all four years; run `.\build\Test-All.ps1`.** Then `git grep -n ContextualTopoButton -- src tests` → nothing.
- [ ] **Step 3: Revit checks (Task 15 rows):**
  - One toposolid selected shows Topo Tools only, with no Floor ⇄ Topo; a floor, a wall, a door, a subdivision or two toposolids show nothing, also right after a toposolid was selected. In the README this is row 18, reworded (Task 15), not a new row.
  - Edit a toposolid's sketch, then select a sketch line: no error, and the shortcut is back after Finish.
  - During a session the shortcut is off the Modify tab; after ✓ it comes back for the toposolid still selected.
  - Fail-safe, whole part: with an empty `%LOCALAPPDATA%\RVTuk\topo-modify-tab-off` and Revit restarted, nothing of ours is on the Modify tab, a whole session (Topo Tools, Draw Lines, Update, Accept, Cancel) works from the Add-Ins tab, and `crash.log` has one line naming the file. Delete the file afterwards.
- [ ] **Step 4: Commit**

`fix(topo-tools): the Modify-tab shortcut is Revit's own panel class and leaves edit modes; Floor ⇄ Topo leaves the tab`

---

### Task 10: Revit — the Topo Edit panel follows the session onto the Modify tab

**Files:**
- Modify: `src/RVTuk.Revit/TopoTools/ModifyTabHost.cs`

**Interfaces:**
- Consumes: `TopoEditPanel.FindOnAddIns()` (Task 5); `ModifyTabAccess.NewRevitPanel` (Task 9).
- Rules:
  - **The copy:**
    - Made on the first `Started`, deferred, in its own try, never inside Apply's fail-safe: `var addIns = TopoEditPanel.FindOnAddIns();` (null is a copy failure, logged as "Topo Edit panel not found on the Add-Ins tab"), then `ModifyTabAccess.NewRevitPanel(addIns.Source)`.
    - Its own `CollapsedPanelImage = TopoIcons.TopoTools(32)` is set in a separate try: a failure there logs one line and keeps the copy, which then folds to the ✗.
    - Made once per Revit session and reused.
    - `Apply` adds it at the end of the Modify tab when `Decide` asks for it, and removes it on `Ended` and in edit modes.
  - **The switch to Add-Ins:** once per session start, after the copy, set the "Add-Ins" tab's `IsActive = true` and read it back. Log once if it did not take.
  - **Copy failure:** `FindOnAddIns` returning null, `NewRevitPanel` throwing, or a throw while adding or removing the copy. Each sets `CopyAvailable = false` for the Revit session and logs one line with the exception's type and message; `Apply` removes the copy if it is still on the tab. The shortcut then stays during a session, and the Add-Ins panel keeps working. A copy failure never switches the host off. `topo-modify-copy-off` (Task 9) gives the same state without trying the copy.
  - **`CopyAvailable`:** true from `Install` unless `topo-modify-copy-off` exists; false for the rest of the Revit session after a copy failure (a private setter; Task 9 declared it `{ get; }`).

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build all four years; run `.\build\Test-All.ps1`.**
- [ ] **Step 3: Revit checks (Task 15 rows), on Modify | Toposolid, Modify | Place Detail Lines, Modify | Text Notes, Modify | Walls and the plain Modify tab:**
  - During a session the copy is there, after Revit's panels, in the tab's colours.
  - ✓ clicked while drawing ends the tool and accepts.
  - There is one Spacing box.
  - The copy's ✗ and ✓ act as Add-Ins' do.
  - After ✓, ✗, a close or another document, the copy is gone.
  - Edit modes during a session: Edit Boundary of another floor with a sketch line selected; a filled region's sketch; Edit Group with an element of the group selected. While each lasts, the copy is off the Modify tab and Revit shows no error. After Finish, and after Cancel, the copy is back, also when nothing is selected afterwards.
  - During a session, with nothing selected, start a new floor and press Cancel Edit Mode. Then open the Modify tab: the copy is on the plain Modify tab.
  - Folded: in a narrow window, and with "Minimize to Panel Buttons", the copy shows the Topo Tools icon, not the ✗, and one click opens ✗ over ✓. After a light/dark switch, the copy's ✗ and ✓ are legible.
  - At the start the ribbon jumps to Add-Ins once; after a selection and Esc it lands on Add-Ins.
  - Fail-safe, copy: with an empty `%LOCALAPPDATA%\RVTuk\topo-modify-copy-off` and Revit restarted, the Add-Ins panel works, and during a session the Topo Tools shortcut stays on the Modify tab and opens the Topo Tools dialog. Delete the file afterwards.
- [ ] **Step 4: Commit**

`feat(topo-tools): the Topo Edit panel follows the session onto the Modify tab`

---

### Task 11: Revit — Topo Tools during a session asks; Accept and Cancel are callable

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/Commands/TopoSessionActions.cs`
- Modify:
  - `src/RVTuk.Revit/TopoTools/Commands/TopoAcceptCommand.cs`
  - `src/RVTuk.Revit/TopoTools/Commands/TopoCancelCommand.cs`
  - `src/RVTuk.Revit/TopoTools/Commands/TopoUpdateCommand.cs` (`Run` loses its unused parameter)
  - `src/RVTuk.Revit/TopoTools/Commands/TopoStartCommand.cs:25-32`

**Interfaces:**
- Produces:
  - `internal static class TopoSessionActions`, with:
    - `internal static bool Accept()`: `TopoUpdateCommand.Run()`, then `TopoSession.End()`. It returns false, with the session still open, when the update did not run.
    - `internal static Result Cancel()`: `TopoCancelCommand`'s whole body, with the same dialogs and results.
  - The commands become one-liners over these.
  - `public static bool TopoUpdateCommand.Run()`.
- Rules for the open-session branch of `TopoStartCommand`:
  - Read the selection before the dialog.
  - Show a TaskDialog titled "Topo Tools", MainInstruction `"Topo Edit is open on {label} in {view name}."`, with command links "Go to it", "Accept it" and "Cancel it" and the spec's descriptions. Esc or closing returns Cancelled and changes nothing.
  - **Go to it:** active view = the session's view; selection = the target; return Succeeded.
  - **Accept it / Cancel it:** run Accept or Cancel. If the session ended and the selection read earlier holds exactly one toposolid or floor other than the old target, go on into the normal start for it. Otherwise return.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build all four years; run `.\build\Test-All.ps1`.**
- [ ] **Step 3: Revit checks (Task 15 rows):** each of the three choices, and closing the dialog.
- [ ] **Step 4: Commit**

`feat(topo-tools): Topo Tools during a session offers Go to it, Accept it, Cancel it`

---

### Task 12: Revit — saving or syncing during a session asks

**Files:**
- Create: `src/RVTuk.Revit/TopoTools/TopoSaveGuard.cs`
- Modify: `src/RVTuk.Revit/Application.cs` (`TopoSaveGuard.Install(application)`, next to `TopoDocumentEvents`)

**Interfaces:**
- Consumes: `TopoSessionActions` (Task 11); `TopoSession.DontAskOnSave` (Task 8).
- Produces: `internal static class TopoSaveGuard { internal static void Install(UIControlledApplication app); }`, hooking `DocumentSaving`, `DocumentSavingAs` and `DocumentSynchronizingWithCentral` with one handler over `RevitAPIPreDocEventArgs`.
- Rules:
  - **When to ask:** only when the session is on `e.Document`, `DontAskOnSave` is not set, and the document is not read-only. Never switch views.
  - **The dialog**, a TaskDialog titled "Topo Edit":
    - MainInstruction `"Topo Edit is still open on {label}"`; MainContent `"Changes to its lines and heights since the last Update are not on it yet."`
    - The command links from Global Constraints.
    - VerificationText `"Don't ask again until this Topo Edit ends"`. No ExtraCheckBoxText or EnableDoNotShowAgain: Revit refuses either together with VerificationText.
    - `AllowCancellation = true`.
  - **The choices:** "Accept, then save" runs `TopoSessionActions.Accept()`; "Cancel the session, then save" runs `Cancel()`; "Save as it is" does nothing.
  - **The box:** if the session still exists afterwards, `DontAskOnSave = WasVerificationChecked()`.
  - **Esc** saves as it is in every case (rule 8 fired on 2026-11-18): never `e.Cancel()`.
  - **Failures:** log one line, and never block the save.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build all four years; run `.\build\Test-All.ps1`.**
- [ ] **Step 3: Revit checks (Task 15 rows):**
  - Each choice; the box.
  - Esc on Save: the save goes ahead as it is.
  - A close answered Yes at "Save changes?", then Esc: the model is saved as it is.
  - Delete one height text in the session's view, close the model, answer Yes at "Save changes?", then choose "Accept, then save": the line's new text reads the last typed height, not 0.00.
  - Synchronize with Central.
  - Saving another document, or a read-only one: no prompt.
- [ ] **Step 4: Commit**

`feat(topo-tools): saving during a session asks first`

---

### Task 13: Revit — Floor ⇄ Topo skips the element being edited

**Files:**
- Modify: `src/RVTuk.Revit/TopoTools/ConvertRunner.cs:89-100`

**Interfaces:**
- Rule: the first check in `RefusalOf`. When the session is on `doc` and `element.Id == session.TargetId`, refuse with `"a Topo Edit session is open on it; Accept or Cancel it first"`. It does not go in `SlabRefusals.Common`, which Update runs on its own target.

- [ ] **Step 1: Implement.** Build all four years; run `.\build\Test-All.ps1`.
- [ ] **Step 2: Revit check (Task 15 row):** convert the session's element together with another floor. The first is skipped with that reason; the second converts.
- [ ] **Step 3: Commit**

`fix(topo-tools): Floor ⇄ Topo skips the element a session is open on`

---

### Task 14: Revit — new topo lines and height texts are orange

**Files:**
- Modify:
  - `src/RVTuk.Revit/Shared/LineStyleCreator.cs:19-24`
  - `src/RVTuk.Revit/TopoTools/TopoLineStyle.cs`
  - `src/RVTuk.Revit/TopoTools/HeightTextType.cs:42-44`
  - `src/RVTuk.Revit/TopoTools/Commands/TopoStartCommand.cs:72`

**Interfaces:**
- Produces: `public static void LineStyleCreator.EnsureExists(Document doc, string name, Color? colour = null)`. It sets `LineColor` only on a style it creates. Auto Dimensions' callers do not change.
- Rule: Topo Tools' orange is defined once, in `TopoLineStyle`, as `new Color(255, 140, 0)`. As a Revit colour integer it is R + G·256 + B·65536 = 36095, which `HeightTextType` sets on the new type's `BuiltInParameter.LINE_COLOR`.

- [ ] **Step 1: Implement.** Build all four years; run `.\build\Test-All.ps1`.
- [ ] **Step 2: Revit checks (Task 15 rows):**
  - In a new project, lines and texts are orange.
  - An existing `_DP-Topo Line` or `_DP-Topo Height` is left as it is.
  - The `_DP-Dim` styles keep the default.
- [ ] **Step 3: Commit**

`feat(topo-tools): new topo lines and height texts are orange`

---

### Task 15: Docs

**Files:**
- Modify: `docs/tools/topo-tools/README.md`, `CLAUDE.md`, `docs/tools/topo-tools/specs/2026-11-15-topo-edit-session-design.md`, `docs/tools/topo-tools/backlog.md`

- [ ] **Step 1: README.**
  - **The workflow:**
    - Step 1: Topo Tools alone on the Modify tab, while exactly one non-subdivision toposolid is selected.
    - Step 2: the panel as one stack plus three buttons and Spacing; a second press asks.
    - Step 3: arming.
    - Step 4: metres from the Survey Point.
    - "A new line's height": 0.00, and survives a close called off.
    - The "ends by itself" sentence: on DocumentClosed.
    - Step 5's "Update and Accept show a summary in a TaskDialog every time": Accept always does, Update only when something needs attention (Task 18).
    - The Height Labels paragraph: its "a text whose line is gone is deleted" stays, and it adds that Update does the same. This is the spec's Docs note on a height text whose line was deleted; Task 17's check row is among the new rows below.
    - The Update paragraph (at HEAD README lines 43-45, starting "**Update** reads each text"; find it by that text, since the edits above shift it): the example becomes "1 line skipped: '42,7x' is not a height in metres.", and "It writes only to the selected element." becomes "It writes only to the toposolid or floor being edited." It also says that Update shows no summary unless something needs attention, and ✓ shows it. The rest stays.
  - **A keyboard-shortcut note beside "Names"** (rule 7 fired: probe 7 found Topo Edit's ids missing from Revit's command list): the Topo Edit buttons cannot be given keyboard shortcuts, because Revit builds that list when it starts and skips hidden panels, and Topo Edit is hidden then. Probe 7 found the RVTuk panel's buttons, Topo Tools among them, in that list: say so.
  - **The updater paragraph:** arming, and Draw Lines from another view.
  - **"Heights are shared (survey) elevations":** as the spec's Docs section says.
  - **"Why the Modify tab needs AdWindows":** the copy during a session, Revit's own panel class, edit modes, the switch to Add-Ins; the shortcut for one non-subdivision toposolid only; no Floor ⇄ Topo; the two switch files (`topo-modify-tab-off`, `topo-modify-copy-off` in `%LOCALAPPDATA%\RVTuk\`) that turn the part off for checking the fail-safe.
  - **"Names":** the shortcut posts only Topo Tools; the copy shares Topo Edit's source.
  - **The Floor ⇄ Topo "Refused" list:** an element with an open session.
  - **The Docs list:** this spec and this plan.
  - **Session checklist, the existing rows:** add 2025, 2026 and 2027 columns. Change:
    - row 2: the dialog;
    - row 3: "type 42.75; the texts read 0.00 and 42.75";
    - row 18: the one row for the toposolid-only shortcut (Task 9's first check, and the spec's): one toposolid shows Topo Tools only, with no Floor ⇄ Topo, doing exactly what its RVTuk-panel button does; a floor, a wall, a door, a subdivision or two toposolids show nothing, also right after a toposolid was selected. Its tail "If nothing shows for a floor, `crash.log` says whether the Modify tab was found" goes: nothing showing for a floor is now the expected result;
    - row 21: the second Update shows no summary, since a "moved by hand" warning would bring one up (Task 18);
    - row 27: "Update twice, then ✓": the second Update shows no summary, and ✓'s summary says "1 point where lines cross took the higher line's height." (Task 18: crossings alone need no attention);
    - row 28: no Floor ⇄ Topo on the Modify tab.
  - **Session checklist, new rows**, from each task's "Revit checks" (Task 9's first check is row 18, not a new row), plus:
    - the heights rows of the spec's "Checks in Revit after the build";
    - two documents open, with the session in one: saving the other asks nothing;
    - Task 17's and Task 18's Revit checks.
  - **The Code table:** in the Revit row (line 291), replace `ContextualTopoButton` with `ModifyTabHost` and `ModifyTabAccess`, and add `TopoDocumentEvents` and `TopoSaveGuard` (`TopoSessionActions` is under `Commands/`). In the Core row (line 289), add the metre height text (`SurveyHeightText`) and the Modify-tab rule (`ModifyTabPanels`).
  - Fix the row numbers cited at README lines 73, 138-139 and 192, and in the backlog's lines 7-8.
- [ ] **Step 2: CLAUDE.md.** Rewrite the "One exception" sentence: the Topo Tools shortcut, shown while exactly one non-subdivision toposolid is selected (today: "one non-subdivision toposolid or one floor"), and the Topo Edit panel during a session, both as Revit's own panel class and fail-safe; the switch to the Add-Ins tab once when a session starts, also through `Autodesk.Windows` and under the same fail-safe; Floor ⇄ Topo is no longer on the Modify tab. In the Topo Tools bullet: the panel order (✗ and ✓ first), "metres, measured like a Survey Point level", and "the toposolid or floor being edited".
- [ ] **Step 3: The 2026-11-15 spec.** Add an "Amended 2026-11-17" paragraph after the 2026-11-16 one, pointing to the new spec. Line 129's "0 means the survey point's elevation" becomes "0 is the zero of Survey Point levels".
- [ ] **Step 4: Backlog.**
  - Verification: the rows of this pass.
  - Ideas: warn when a line's height is far from the element's surface; hide the panel only after Revit has listed its commands (rule 7 fired: probe 7 found no Topo Edit ids in that list).
  - Done: this pass.
- [ ] **Step 5: Commit**

`docs(topo-tools): Topo Edit follows the session; heights in metres`

---

### Task 16: The whole branch, then Revit

- [ ] **Step 1: Check the branch.**
  - `.\build\Test-All.ps1` → all pass; all four years build with 0 errors.
  - `git grep -n "TopoHeightParser\|TopoLengthFormatter\|ContextualTopoButton\|TopoRibbonProbe\|TopoProbeCommand\|SkipIconsOnlyLook" -- src tests` → nothing.
  - `git grep -n "ContextualTopoButton\|TopoHeightParser\|TopoLengthFormatter" -- CLAUDE.md docs/tools/topo-tools/README.md docs/tools/topo-tools/backlog.md` → nothing.
  - `git grep -n "is not a height\"\|selected element" -- docs/tools/topo-tools/README.md` → nothing.
  - If probe 9's rule fired: `git grep -n "SurveyPointMarkerMetres\|MarkerNoted" -- src tests` → nothing.
  - `.\build\Test-Staging.ps1` only if a project file changed (none is planned).
- [ ] **Step 2: Whole-branch review** against the spec (superpowers:requesting-code-review). Each fix is its own commit.
- [ ] **Step 3: Daniel's checks in Revit.**
  - In an elevated shell with Revit closed, Daniel runs `.\Deploy.ps1 2024` from `D:\Coding\RVTuk` and works through the README rows.
  - Each fix is its own commit.
  - Ticked rows go in with `docs(topo-tools): the follow-the-session pass checked in Revit 2024`.
  - Then 2025, 2026 and 2027, as Revit is at hand.
  - Once Daniel's Revit checks pass, delete the probe branch: `git branch -D topo-probe-2026-11-17`.
- [ ] **Step 4: Update the description of [tchonky/RVTuk#16](https://github.com/tchonky/RVTuk/pull/16)** with this pass.

---

### Task 17: Revit — a height text whose line was deleted no longer breaks Update

Daniel's report 4 in the spec's "Probe results". The cause (crash.log, 2026-11-18): `Label` builds `textById` from the collected texts after `doc.Delete(toDelete)`; reading the deleted note's `Id` throws `InvalidObjectException`, and Update (and Height Labels, which calls `Label` too) is rolled back.

**Files:**
- Modify: `src/RVTuk.Revit/TopoTools/HeightTexts.cs` (`Label`)

**Interfaces:**
- Produces: nothing new.
- Rules:
  - Every map keyed by a collected text is built before anything is deleted, and a deleted note's object is never read afterwards. `HeightTextLinker`'s contract already keeps a deleted text out of `Relinked` and `LineByText`.
  - Also check every other `doc.Delete` / `Document.Delete` in `src/RVTuk.Revit` for a read of a deleted element's managed object after the delete; fix any the same way, or report that there is none.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build all four years; run `.\build\Test-All.ps1`.**
- [ ] **Step 3: Revit checks (Task 15 rows):** delete a topo line but keep its height text, then Update: no error, the text is deleted, the line's points come off the element; the same with Height Labels.
- [ ] **Step 4: Commit**

`fix(topo-tools): a height text whose line was deleted no longer breaks Update`

---

### Task 18: Core and Revit — Update shows its summary only when something needs attention; Accept always does

Spec item 12, from Daniel's report 3 in the spec's "Probe results".

**Files:**
- Modify:
  - `src/RVTuk.Core/TopoTools/TopoSessionReport.cs`
  - `src/RVTuk.Revit/TopoTools/Commands/TopoUpdateCommand.cs`
  - `src/RVTuk.Revit/TopoTools/Commands/TopoSessionActions.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/TopoSessionReportTests.cs`

**Interfaces:**
- Produces:
  - `public bool ShowAfterUpdate => HasProblems || LinesApplied == 0;` on `TopoSessionReport`.
  - `public static bool TopoUpdateCommand.Run(bool alwaysShowSummary)`: the button's `Execute` passes false, `TopoSessionActions.Accept()` passes true.
- Rules:
  - The summary dialog shows when `alwaysShowSummary || report.ShowAfterUpdate`; otherwise Run shows nothing after a committed update. Refusals, a rolled-back commit and failures keep their dialogs.
  - The Survey Point marker is read, and `session.MarkerNoted` set, only when a summary is about to be shown; the "one read per session, even a failed one" rule stays.
  - The class comment of `TopoSessionReport` stops saying it is shown "after each run".

- [ ] **Step 1: Write the failing tests.** `ShowAfterUpdate` is true for each problem alone (an unreadable height, a point moved by hand, a lost point, an unlinked text, each with `LinesApplied = 1`), true with nothing applied and no problem, and false with lines applied and no problem, also with points dropped at crossings and with a marker figure set.
- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter TopoSessionReportTests` → FAIL.

- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests to see them pass**

Run: the same filter → PASS. Then build all four years and run `.\build\Test-All.ps1` → all pass.

- [ ] **Step 5: Revit checks (Task 15 rows):** Update with nothing to report shows nothing; ✓ shows the summary, with the marker's line when it applies; an Update with a skipped line shows the summary.
- [ ] **Step 6: Commit**

`feat(topo-tools): Update shows its summary only when something needs attention; Accept always does`
