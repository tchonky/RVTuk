# Project Comparator — Product Definition & Roadmap

Product lens. See the [design spec](../superpowers/specs/2026-06-23-project-comparator-design.md) for architecture, [guidelines.md](guidelines.md) for domain rules, [ui-ux.md](ui-ux.md) for screens.

---

## 1. Problem & users

Knafo Klimor's single master Revit template has fallen behind the firm's real standards. Dozens of live projects each carry evolved, more-complete versions of view templates, browser organization, parameters, schedules, etc., never back-ported. Result: new projects start stale; ongoing projects drift; there is no in-Revit way to compare two models' standards. The BIM manager does this by hand today.

| User | Role |
|------|------|
| **BIM Manager (primary)** | Runs Build-Template to harvest best-of-breed into the Standard; reviews reports. |
| **Project Architect (secondary)** | Runs Audit-Project to compare an open project against the Standard and close gaps. |

**Jobs-to-be-done:** (1) build a better template from several projects; (2) audit a project against the template; (3) make raw Revit diffs readable and actionable.

---

## 2. MVP (v1)

A ribbon button "Project Comparator" opening a window where the user captures/loads two snapshots (active doc, another open doc, a closed file, or the Standard), runs a **View Templates** comparison, and gets a structured side-by-side report with completeness scores, dependency manifests, and per-item recommendations. In Build-Template mode the user **accepts** chosen templates (with dependencies) into the editable **Standard**. Report exports to self-contained HTML. **Nothing is written to any Revit model.**

---

## 3. MoSCoW

### Must
- Ribbon button (separate from Family Browser) + Comparator window.
- Snapshot capture: active document; another open document; background-open a closed `.rvt`/`.rte` (with safeguards — see spec §5.3).
- View Templates: extract → match (name within view-type) → **inclusion-aware** field-level diff.
- Completeness scoring per template (documented rubric).
- **Dependency manifest** per differing/recommended template; flag non-resolving deps.
- Shared-parameter inventory (GUID-keyed) captured as dependency data.
- **Standard building:** accept whole items + dependency closure into the editable Standard, with per-item provenance and replace/keep conflict handling. Standard persists in SQLite.
- Audit: compare a project snapshot against the Standard.
- Report panel (side-by-side, status icons, scores) + **self-contained HTML export**.
- Recommendation per matched-and-differing item (overridable); honest "no fake newer".
- Builds & runs on Revit 2023/2024/2025; **zero writes to Revit models** (byte-identical files after a run).
- Graceful failure (missing/locked/wrong-version file → clear message, no crash).

### Should
- Full V/G per-category override drill-down (v1 may ship "overrides differ" only).
- Field-level cherry-pick/merge into the Standard (filters from X, V/G from Y).
- Standard revision history + rollback.
- CSV export; "open in Revit" jump to a template; rename-detection hint (fingerprint).
- Summary header (counts); re-run without re-picking; remember last-used sources.

### Could
- Red/green inline field highlighting; filter report by status; configurable scoring rubric (firm JSON); firm logo in HTML report.

### Won't (v1)
- Any write-back to Revit models.
- Materializing the Standard into a real `.rte`/`.rvt`.
- Other categories (Browser Org, Parameters, Schedules, Sheets, Detail Views) — scaffolded only.
- Batch/multi-project parallel comparison.
- Cross-session persistence of transient *decisions* (the Standard itself persists).
- Family comparison (Family Browser owns it).

---

## 4. User stories (v1)

**Build Template**
- As a BIM Manager, I select two project snapshots and compare their View Templates to see which has the more complete version of each. (US-01)
- …so I can see templates present in one but not the other and add the gaps to the Standard. (US-02)
- …with a per-template field-level diff (scale, detail level, V/G, filters, etc.) so I can decide what to adopt. (US-03)
- …with a recommendation and completeness score as a starting point. (US-04)
- I **accept** a chosen template (and its dependencies) into the Standard, with provenance recorded. (US-05)
- I export the report to HTML to share/track. (US-06)

**Audit Project**
- As a Project Architect, I compare my open project against the Standard to see where it deviates. (US-07)
- …see templates the Standard has that my project is missing. (US-08)
- …see where my templates differ from the Standard so I can correct them before delivery. (US-09)
- …read the report inside Revit without switching tools. (US-10)

---

## 5. End-to-end journeys

### A. Building / improving the master template
1. Open Revit (master/Standard context).
2. Click "Project Comparator".
3. Mode = **Build Template**.
4. Side A = capture `Project-Alpha.rvt`; Side B = the **Standard** (or `Project-Beta`).
5. Compare → snapshots captured (ExternalEvent-marshaled, progress shown); diff runs on background thread.
6. Report loads: summary + per-template sections grouped by status.
7. Expand a differing template → field-level diff, completeness, dependency manifest, recommendation.
8. **Accept** the better version into the Standard (dependencies pulled; provenance recorded; conflicts resolved).
9. Repeat across templates/projects; the Standard accumulates best-of-breed.
10. Export HTML as the record. (Later phase: materialize the Standard into a real Revit template.)

### B. Auditing a live project against the template
1. Project is the active document.
2. Click "Project Comparator"; Mode = **Audit Project**.
3. Side A = active document (read in memory, no re-open); Side B = the **Standard** snapshot (no file opened).
4. Compare → drift report.
5. Expand "Section – Interior" (differs) → recommendation "Standard preferred"; expand "Only in Standard" to see missing templates.
6. Decide which gaps are intentional vs to-fix; export HTML for the team.
7. Fix in Revit by hand using the report as spec; re-audit to confirm.

---

## 6. Acceptance criteria (v1, all testable report-only)

- **AC-01 Capture:** valid sources compared without crashing; report within ~60s for models <50MB each; progress shown.
- **AC-02 Enumeration:** every view template in each snapshot is listed; counts match Manage ▸ View Templates in Revit.
- **AC-03 Match:** same-name (same view-type) templates match; differing names list as one-sided; no false/missed matches.
- **AC-04 Diff correctness (inclusion-aware):** correctly identifies controlled-vs-excluded and value differences for scale, detail level, discipline, V/G category visibility/overrides, applied filters, underlay, display style; identical controlled fields are not reported as differences.
- **AC-05 Completeness & recommendation:** every differing pair has a completeness profile and an overridable recommendation; never asserts "newer" as fact.
- **AC-06 Dependency manifest:** each recommended/differing template lists its dependencies and flags any that don't resolve.
- **AC-07 Standard build:** accepting an item copies it + its dependency closure into the persisted Standard with provenance; a name conflict prompts replace/keep.
- **AC-08 No writes:** both source models are byte-identical (hash unchanged) after any run.
- **AC-09 Active doc:** "use active document" reads the open model without re-opening from disk.
- **AC-10 Export:** HTML export opens in a browser and faithfully reproduces the report.
- **AC-11 Multi-version:** builds and runs correctly on Revit 2023/2024/2025.
- **AC-12 Graceful failure:** missing/locked/newer-version file → clear message, Revit not destabilized.

---

## 7. Roadmap

| Phase | Content | Value | Risk |
|-------|---------|-------|------|
| **1 (v1)** | Report-only; View Templates; build the Standard (accept items + deps) | First automated cross-model diff; build the master inside the tool | Headless open of workshared/old files (mitigated, spec §5.3) |
| **2** | More categories (Parameters → Filters → Schedules → Browser Org → Sheets) | Each adds a job-to-be-done; params shore up the dependency substrate | Per-category API surface & diff semantics |
| **3** | **Write-back**: materialize the Standard / apply selected items into a Revit model | Removes the manual "now do it in Revit" step | Revit transactions can corrupt/conflict; gate per item with confirm |
| **4** | Bulk sync / push Standard to many projects | Firm-wide drift becomes solvable on a cadence | Batch over workshared models; needs rollback + audit logging |

---

## 8. Integration with Family Browser
Families are already solved (Family Browser handles `.rfa` indexing/browsing/load/audit). The Comparator does **not** re-implement family comparison. In the category rail, "Families" links out to the Family Browser. When a Families category is eventually added (phase 2), it **adapts the existing family index** (`BrowserRepository`) rather than re-scanning `.rfa`. Both features are peers on the same RVTuk ribbon panel. Shared WPF controls, if they emerge, get extracted into `RVTuk.UI` in phase 2.

---

## 9. Success metrics, assumptions, risks

**Metrics:** BIM Manager runs Build-Template ≥1×/quarter and acts on it; fewer "stale template" complaints; report generation <60s typical; zero data-loss incidents; spot-check of 5 diffs finds 0 errors.

**Assumptions:** headless detached read-only open works from an ExternalEvent (spike to confirm before committing); view-template names unique within a model (Revit enforces) so name-match is unambiguous within a file; shared-drive file access via the Windows user; completeness (not modified-date) is the recommendation basis.

**Risks:** see spec §7. Solo-dev bandwidth → keep v1 tightly scoped to View Templates + Standard building; resist adding categories or write-back before v1 is validated.
