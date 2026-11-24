# Topo Edit Height Box Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Height box in the Topo Edit panel that shows the selected topo lines' height and, on Enter, rewrites their height labels.

**Architecture:** The label text stays the only store of a height. The box reads labels on `SelectionChanged` and writes labels on `EnterPressed` (one transaction); the existing session updater, Update, Accept and Cancel are unchanged. The one pure rule (what the box shows for a set of labels) lives in Core and is unit-tested.

**Tech Stack:** C#, Revit API 2024–2027 (ribbon `TextBox`, `UIControlledApplication.SelectionChanged`), xunit.

**Spec:** `docs/tools/topo-tools/specs/2026-11-23-height-box-design.md`

## Global Constraints

- Branch `topo-tools`; no other branch, no worktree.
- Height text format and parsing are `SurveyHeightText.Format` / `TryParse` only (metres, comma or point, optional `m`, −500..3000 m).
- Enter only changes labels; it never applies points (Update/Accept do).
- All edits of one Enter are one transaction named `Topo height`.
- Every ribbon/event handler catches and logs through `Application.LogDiagnostic`, like the Spacing box.
- `.\build\Test-All.ps1` passes, and `RVTuk.sln` builds for Release2024–Release2027.

## Review Focus

- Labels "42.7" and "42.70" on two selected lines → the box shows `42.70` (same height), not empty. Pinned in Task 1.
- A selected line whose label is unreadable (someone typed "abc") → box empty; Enter still writes the new height to it. Pinned in Task 1 (show) and the manual check (write).
- A selection that holds both a line and its own label → that line counts once, gets one label (no duplicate text). Manual check row.
- Enter with a line that has no label → a label is created, and Cancel deletes it again (it goes in `session.Created`). Manual check row.
- Selection in another document or view than the session's → box empty and disabled-looking (empty), Enter changes nothing. Manual check row.

---

### Task 1: What the box shows (Core)

**Files:**
- Create: `src/RVTuk.Core/TopoTools/HeightBoxText.cs`
- Test: `tests/RVTuk.Core.Tests/TopoTools/HeightBoxTextTests.cs`

**Interfaces:**
- Produces: `public static class HeightBoxText { public static string For(IEnumerable<string?> labels); }` in namespace `RVTuk.Core.TopoTools`. Returns `SurveyHeightText.Format` of the shared height when every label parses to the same height (compared after `Format`, so "42.7" == "42.70"); `""` when the list is empty, any label is unreadable or null, or the heights differ.

- [ ] **Step 1: Write the failing tests**

```csharp
public class HeightBoxTextTests
{
    [Fact] public void EmptySelectionShowsNothing() => Assert.Equal("", HeightBoxText.For(new string?[0]));
    [Fact] public void OneLabelShowsItsHeight() => Assert.Equal("42.75", HeightBoxText.For(new[] { "42,75 m" }));
    [Fact] public void SameHeightWrittenTwoWaysShowsOnce() => Assert.Equal("42.70", HeightBoxText.For(new[] { "42.7", "42.70" }));
    [Fact] public void DifferentHeightsShowNothing() => Assert.Equal("", HeightBoxText.For(new[] { "42.70", "43.00" }));
    [Fact] public void AnUnreadableLabelShowsNothing() => Assert.Equal("", HeightBoxText.For(new[] { "42.70", "abc" }));
    [Fact] public void AMissingLabelShowsNothing() => Assert.Equal("", HeightBoxText.For(new[] { "42.70", null }));
}
```

- [ ] **Step 2: Run, expect a build failure (`HeightBoxText` not defined)**

Run: `dotnet test tests\RVTuk.Core.Tests\RVTuk.Core.Tests.csproj -c Release2025 --filter HeightBoxTextTests`

- [ ] **Step 3: Implement `HeightBoxText.For` in `src/RVTuk.Core/TopoTools/HeightBoxText.cs`**

- [ ] **Step 4: Run the same command; expect 6 passed**

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Core/TopoTools/HeightBoxText.cs tests/RVTuk.Core.Tests/TopoTools/HeightBoxTextTests.cs
git commit -m "feat(topo-tools): what the Height box shows for a set of labels"
```

### Task 2: The Height box in the panel (Revit)

**Files:**
- Modify: `src/RVTuk.Revit/TopoTools/HeightTexts.cs` (add `ReadNotes`, make `ReadHeights` use it)
- Modify: `src/RVTuk.Revit/TopoTools/TopoEditPanel.cs` (box, selection refresh, Enter)

**Interfaces:**
- Consumes: `HeightBoxText.For` (Task 1); existing `HeightTexts.CreateFor(doc, view, line, sharedFeet)`, `HeightTextLinkStore.TryGetLineId(TextNote, out long)`, `HeightTextType.IsHeightText`, `TopoLineCollector.Collect(doc, view)` (`TopoLineCandidate.LineId`), `TopoSession.Current` (`IsFor`, `ViewId`, `Created`, `LastSeenTexts`), `LastTypedHeights.Set`.
- Produces: `public static IReadOnlyDictionary<long, TextNote> HeightTexts.ReadNotes(Document doc, View view)` — line id → its height text, by the same link plan as `ReadHeights`; `ReadHeights` becomes a projection of it (text trimmed).

- [ ] **Step 1: Add `HeightTexts.ReadNotes` and rewrite `ReadHeights` on top of it.** Build: `dotnet build src\RVTuk.Revit\RVTuk.Revit.csproj -c Release2024` — 0 errors.

- [ ] **Step 2: Add the box in `TopoEditPanel.Create`, right after Spacing:** `TextBoxData("TopoHeight")`, `PromptText = "Height (m)"`, `ShowImageAsButton = false`, `SelectTextOnFocus = true`, `Width = 80`, tooltip `"Height of the selected topo lines, in metres. Type a height and press Enter to give it to all of them; Update or Accept moves the points."`. Hook `EnterPressed += OnHeightEntered`, `app.SelectionChanged += OnSelectionForHeight`, and clear the box (`Value = ""`) on `TopoSession.Started` and `TopoSession.Ended`. Keep it in `private static TextBox? _heightBox`.

- [ ] **Step 3: `private static List<long> SelectedLineIds(Document doc, View view, ICollection<ElementId> selected)`** — the topo line ids of `TopoLineCollector.Collect(doc, view)` that are selected, plus the line of each selected height text owned by `view` (`HeightTextLinkStore.TryGetLineId`) that is still a topo line; distinct. Empty when no session or `!session.IsFor(doc)`.
  `// ponytail: collects the view's topo lines on every selection change; cache per session if it lags on big sites.`

- [ ] **Step 4: `OnSelectionForHeight`** — sets `_heightBox.Value = HeightBoxText.For(lineIds.Select(id => notes.TryGetValue(id, out var n) ? n.Text : null))` with `notes = HeightTexts.ReadNotes`; empty when no session lines are selected. Try/catch → log `"Topo Edit height box failed: ..."`.

- [ ] **Step 5: `OnHeightEntered(object? sender, TextBoxEnterPressedEventArgs e)`** — with `e.Application.ActiveUIDocument`: if the box does not parse with `SurveyHeightText.TryParse`, or no session line is selected, put back what the selection shows and stop. Otherwise, in `Transaction(doc, "Topo height")`: for each selected line, set its note's `Text = SurveyHeightText.Format(feet)` when different; when it has no note, `HeightTexts.CreateFor(...)` and add the new id to `session.Created` and `session.LastSeenTexts` (trimmed text). Then `LastTypedHeights.Set(doc, feet)` and show `Format(feet)` in the box.

- [ ] **Step 6: Build every Revit year**

Run: `dotnet build RVTuk.sln -c Release2024`, then `-c Release2025`, `-c Release2026`, `-c Release2027` — each 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/RVTuk.Revit/TopoTools/HeightTexts.cs src/RVTuk.Revit/TopoTools/TopoEditPanel.cs
git commit -m "feat(topo-tools): Height box in the Topo Edit panel"
```

### Task 3: Docs, full test run, Revit check list

**Files:**
- Modify: `docs/tools/topo-tools/README.md` (panel description near line 19, a **Height** paragraph after the Spacing one near line 219, `TopoHeight` in the ids list near line 386, new manual-check rows after row 15's table)
- Modify: `CLAUDE.md` (Topo Tools bullet: panel order "…Update, Spacing, Height")

- [ ] **Step 1: Write the docs.** Manual-check rows, one each: one line → box shows its height, type + Enter changes the label; two lines with different heights → box empty, Enter gives both the height in one undo; a line without a label → Enter creates one, Cancel removes it; line plus its own label selected → one label; "abc" typed → box shows the selection's height again, nothing changes; Modify-tab copy shows the Height box too.

- [ ] **Step 2: Run everything**

Run: `.\build\Test-All.ps1` — all pass on net48, net8, net10.

- [ ] **Step 3: Commit**

```bash
git add docs/tools/topo-tools/README.md CLAUDE.md
git commit -m "docs(topo-tools): the Height box"
```
