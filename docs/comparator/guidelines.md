# Project Comparator — BIM Domain Guidelines

Domain rules from the BIM Manager lens. These define **what** to compare and **how to judge** it, independent of code. See the [design spec](../superpowers/specs/2026-06-23-project-comparator-design.md) for architecture and [features.md](features.md) for scope.

---

## 1. Guiding principles

1. **Compare side by side; never fabricate "newer."** Revit exposes no reliable per-element modified timestamp. The tool surfaces *differences* and *completeness*; the human decides which is canonical. Capture-date is shown only as honest provenance.
2. **Inclusion-awareness before values.** Most categories distinguish "controls this setting" from "value = X". Read what is controlled first, or you generate false differences.
3. **Comparison is transitive.** An item is only truly portable if its dependencies resolve in the target. Build the diff as a dependency graph, not a flat field list.
4. **Match confidence is explicit.** Every matched pair states its basis (name / GUID / fingerprint-suggested). Never match silently.
5. **The report is a checklist.** In report-only v1 the human re-creates changes by hand (or accepts items into the Standard), so the report must be complete, ordered stably run-to-run, and exportable.

---

## 2. Per-category comparison model

| Category | Items | Matching key | "Better / more complete" means |
|----------|-------|--------------|--------------------------------|
| **View Templates** | each `View` with `IsTemplate==true` | **Name** within `ViewType` bucket; `UniqueId` as in-session tiebreaker; content-fingerprint to *suggest* renames | See §3 — multi-axis: # of meaningfully controlled fields, breadth of V/G & filter coverage, validity of references |
| **Project / Shared Parameters** | each binding in `BindingMap` | **Shared: GUID** (authoritative). **Project: Name+Type+Group** (low confidence) | Bound to more/correct categories, correct instance-vs-type, correct group, present in shared-param file |
| **Browser Organization** | each named scheme (project browser + sheet browser separately) | **Name** | Richer valid grouping/sort/filter; grouping fields that exist in the target |
| **Schedules** | each `ViewSchedule` | **Name** + scheduled category (built-in id) | More fields, correct sort/group, filters referencing valid params, formatting set |
| **Sheets / titleblocks** | titleblock families/types; sheet defs | Titleblock: **family+type name**; Sheet: number/name | Titleblocks: newest type with correct label params. Individual sheets rarely templatable |
| **Detail views / components** | drafting views, detail items | Name (view); family/type (component) | More complete 2D content with travelling pattern refs; high duplication risk |
| **Families** | — | **Handled by the Family Browser** | Defer to existing version logic; Comparator only links out |

---

## 3. View Templates — deep dive (v1 category)

### 3.1 What a view template contains
Two distinct layers — getting this distinction right is the core of the feature:

- **A. The controlled set** — only the parameters whose "include" checkbox is ticked are governed (`GetNonControlledTemplateParameterIds()` / controlled set). An *excluded* field means "template does not control this" — semantically different from "controls it, value = X".
- **B. The values of controlled fields** (varies by view type):
  - View scale, detail level, parts visibility, discipline, display model, visual/model graphics style
  - **V/G overrides — Model categories** per category *and subcategory*: visibility, projection/cut line weight·color·pattern, projection/cut fill (fore+back pattern·color), transparency, halftone, detail-level override
  - **V/G overrides — Annotation / Analytical / Import / Filters / Worksets / RVT Links** tabs (each its own surface)
  - **Filters**: the ordered applied list (`GetFilters()`) + per-filter enabled (`GetIsFilterEnabled`), visibility (`GetFilterVisibility`), overrides (`GetFilterOverrides`). The filter *definition* (`ParameterFilterElement`) is a doc-level travelling dependency.
  - Model display / shadows / sketchy lines / lighting / photographic exposure (view-type dependent)
  - Phase + phase filter; view range (plan); underlay; crop/annotation-crop visible & active; far clip (sections); section box (3D); color schemes (room/area/space); sun path (3D); workset visibility (workshared only — non-portable)

### 3.2 Field classification (per field, for a matched pair)

| Outcome | Condition |
|---------|-----------|
| Identical | controlled in both, equal |
| Differs (value) | controlled in both, different |
| A-only control | controlled in A, excluded in B |
| B-only control | controlled in B, excluded in A |
| Both uncontrolled | excluded in both → ignore |
| Unmatched ref | controlled in both but references an element (filter/pattern) that doesn't resolve identically → dependency conflict, not a value diff |

V/G overrides compare per `(category, subcategory)` cell and report at cell granularity ("Walls > cut pattern differs"). Filters compare as an ordered set keyed by filter name; report added/removed/reordered/override-differs separately.

### 3.3 "Which is better" — rule set (produce a profile, not a single number)

1. **Validity gate (first).** A template referencing a filter/pattern/parameter that doesn't resolve in the target carries a **dependency cost** flag. Internally-valid beats equally-complete-but-broken. Never recommend importing a template whose dependencies don't travel without surfacing them.
2. **Completeness score** = weighted count of *meaningfully* controlled fields: V/G category overrides and filters-with-overrides high; view range / phase / detail level / scale / discipline medium; render cosmetics low. **"More included fields" ≠ better** — a template may be intentionally looser. Present as "controls N of M, here's which"; never auto-pick on completeness alone.
3. **Recency** — no reliable timestamp. Show snapshot capture-date with its basis; the canonical source is the human's assertion in Build-Template mode. **Never claim "newer" as fact.**
4. **Recommendation** per pair: `RECOMMEND A | RECOMMEND B | MERGE (e.g. filters from A, V/G from B) | REVIEW (conflicting refs)`. MERGE is the realistic common answer; the report must support field-level cherry-picking.

### 3.4 Edge cases (handle explicitly)
- Same name, different content (the common case — this *is* the diff).
- Renamed templates → name-match misses them; use content-fingerprint to *suggest* "possible rename", never auto-merge.
- View-type specificity → match within `ViewType` buckets; cross-type name collisions are flagged, not diffed.
- Dependency travel → every recommended template lists its full dependency manifest.
- Templates assigned to views (`View.ViewTemplateId`) → note "controls N views" as impact context for the eventual apply phase.
- Default/built-in vs custom → don't recommend migrating Revit OOTB noise.

---

## 4. Cross-cutting dependency gotchas

1. **Templates → filters → parameters → shared-param GUID.** Resolve the chain; mark a template "portable" only if every link resolves. Diff is a graph.
2. **Project parameters have no GUID.** Two confidence tiers: GUID-exact (shared) vs heuristic (project). Mark project-param matches low-confidence; suggest promoting key project params to shared.
3. **Browser organization is a named scheme.** Validate grouping/folder fields against the target's parameter set before recommending.
4. **Titleblocks are families.** Route version questions to the Family Browser.
5. **Line/fill patterns, materials, object styles are shared, by-name, referenced widely.** Surface them bottom-up as dependencies. **Name-match-but-definition-differs** is a real hazard (by-name import silently reuses the target's definition) — flag it.
6. **No reliable "newer" timestamp.** State this plainly in the report. Use completeness + human-asserted canonical source.
7. **`UniqueId` is not portable identity.** Cross-doc matching is name/content based.
8. **Worksets are project-specific.** Treat workset-visibility diffs as low priority unless both docs are workshared with matching workset names.

---

## 5. The Standard (curation rules)

When the user **accepts** a view template into the Standard:

1. **Enforce dependency closure.** Pull the template's filters → their parameters (with shared-param GUIDs) → line/fill patterns / subcategories. A template cannot enter the Standard with dangling dependencies; the tool either pulls them too or flags the gap.
2. **Record provenance per item** (and per field when merged): which source snapshot each piece came from. The BIM manager must be able to read "this template ← Project Gamma; its Structural filter ← Project Alpha".
3. **Conflict on accept:**
   - Name already in the Standard → replace / keep existing / (field-merge: Should).
   - A pulled dependency whose name matches an existing Standard dependency but whose definition differs → conflict flag; user chooses which definition wins. Document the choice in provenance.
4. **Re-import survives edits.** Re-snapshotting a source later and re-curating must preserve existing Standard edits; the tool shows what's new/changed in the source vs what the Standard already holds.
5. **Materialization (future).** The Standard's shape must remain a faithful superset of what write-back needs to recreate the item in a real Revit template — so closure + provenance are mandatory, not optional.

---

## 6. Real-world workflows

### 6.1 Build the master template (v1 priority)
1. Snapshot the outdated master + 3–6 good projects (dated).
2. Pick a baseline (usually the current master/Standard).
3. Per category, compare master vs each candidate → matched / unmatched / differs tables.
4. Triage View Templates: per name see present-where, completeness profile, field-level diffs, dependency manifest. **Accept** the best version (or cherry-pick) into the Standard.
5. Resolve dependencies as you accept (closure rules, §5).
6. Re-snapshot/re-compare against the Standard; loop closes when the Standard holds the best-of-breed set. (Materializing into a real Revit template = later write-back.)

### 6.2 Audit a project against the Standard (phase 2 framing)
1. Snapshot the project; compare against the Standard (master = authority).
2. Drift report: templates missing vs standard; present-but-differing (field-level); non-standard extras; broken dependencies.
3. Remediate in Revit by hand; re-audit to confirm.

### 6.3 A report must contain
Match basis + confidence · inclusion-aware diffs · field-level granularity with Revit-accurate names · full dependency manifest per recommended item · honest recency stance · a recommended action per item (overridable) · unmatched/extra items on both sides · export (HTML/CSV) · stable ordering.

---

## 7. Prioritization beyond View Templates
1. **View Templates** (v1) — highest leverage.
2. **Project/Shared Parameters** — the dependency substrate for filters/schedules; shared-param GUID gives the cleanest matching.
3. **View Filters (standalone)** — tightly coupled to templates/params; needed to make template migration executable.
4. **Schedules** — high day-to-day value, self-contained.
5. **Browser Organization** — small, high visible payoff, low effort (could leapfrog as a quick win).
6. **Titleblocks/Sheets** — titleblocks via Family Browser; individual sheets rarely templatable.
7. **Detail components/views** — high duplication, fuzzy matching, lower ROI.
8. **Line/fill patterns, materials, object styles** — surface as dependencies first; promote to standalone only on demand.
