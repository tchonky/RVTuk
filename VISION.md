# RVTuk — Product Vision

> Top-level product doc — what RVTuk is, who it's for, and where it's going.
> For build/architecture/threading/deploy see [`CLAUDE.md`](CLAUDE.md) (authoritative technical reference).
> For the live task list see [`docs/BACKLOG.md`](docs/BACKLOG.md).

---

## What RVTuk is

One Revit add-in toolkit for **Knafo Klimor Architects LTD** that makes the firm's BIM
work faster and more consistent: managing the family library, standardizing project
settings, and automating repetitive drafting — all on **one ribbon, behind one install**.

Supports **Revit 2024 and 2025** simultaneously (Revit 2023 was dropped).

---

## Who it's for

- **You (BIM lead)** — the primary builder and power user today.
- **The BIM Manager** — curates the firm's standards; harvests best-of-breed settings
  from real projects into a single curated "Standard."
- **Project Architects** — find and place families correctly, audit their projects
  against the Standard, and run the productivity tools day to day.
- **Eventually, other architecture firms** — if RVTuk reaches the commercial stage.

---

## Ambition — three phases

| Phase | Goal | State |
|-------|------|-------|
| **(a) Personal tool** | Power-user tool for you and a colleague or two. Optimize for your own speed. | **Now** |
| **(b) Firm toolset** | Polished, one-click-installable internal product the whole office uses — onboarding, support, the works. **Ship as soon as a pillar is workable.** | **The active goal** |
| **(c) Commercial product** | Licensed/sold to other firms. Only pursued if (b) genuinely proves itself. | Someday / stretch |

**Guiding rule:** every feature should be shippable to the *whole firm*, not just to one
expert. That bar — "a non-expert architect can install it and succeed" — is what
separates (a) from (b), and it's the one to keep aiming at.

There is no fixed timeline. The order is what matters, not the dates.

---

## The three pillars

### 1. Family Management — *Family Browser*
Index the firm's `.rfa` library (category, parameters, thumbnails) into a shared
database; search/filter/browse it from a dark-themed panel; load or update families
straight into the active project; keep per-family instructions, tags, favourites, and
custom thumbnails.
**Status:** shipped in v1, together with **Rishui Zamin** (the רישוי זמין area-submission
export; ribbon button "Area Calc" — see `docs/tools/rishui-zamin/`).

### 2. Standardization — *Project Comparator* (the "Template Snapshot")
Capture a snapshot of a project's most important settings — **view templates first**,
then project/shared parameters, schedules, browser organization, sheets — store it, and
either **compare two projects** or **audit a project against a curated firm "Standard."**
The BIM Manager builds the Standard by accepting best-of-breed items (with their
dependencies) out of live projects. Report-only today; **writing the Standard back into
models is a deliberate, gated future phase**.
**Status:** moved out of RVTuk into its own separate project (July 2026, while preparing
the v1 launch — RVTuk v1 ships only the Family Browser and Area Calc). The comparator
code, tests, and specs were stripped from this repo; recover them from git history when
seeding the new project.

### 3. Productivity tools
Focused, interactive daily-work buttons:
- **Room renumbering** — interactive, by level / by category / by any parameter (smarter
  than a fixed script).
- **Auto-dimensioning** — create and maintain clear dimensions on defined views, and
  regenerate them on demand. This folds in the existing separate **`KKimensions` /
  DimensionPropagator** project rather than reinventing it.
- **Instructions** — editable, in-Revit visual pages with tips and how-to guidance.

**Status:** planned.

---

## Roadmap (near-term order)

1. ~~Finish + verify Family Explorer~~ — **done**: merged and shipped in v1 as the Family Browser.
2. **Land Project Comparator v1** *as its own separate project* — View Templates, build the Standard, report-only (zero writes to models). Tracked in that project's repo, not here.
3. **Productivity buttons** — interactive room renumbering, then **absorb `KKimensions` auto-dimensions** into RVTuk.
4. **In-Revit Instructions** feature.
5. **Comparator phase 2+** — more categories, then eventual gated write-back (tracked in the separate comparator project).
6. **Toward phase (b)** — proper installer, onboarding, and firm-wide rollout.

---

## Brand & naming

- **`RVTuk` is the locked internal/product name.** **`ReviTchucky` is retired** (old name; being cleaned up).
- Keep the **user-facing display name as a single swappable string** so a commercial
  rebrand (phase c) costs minutes.
- The **deep identity** — namespaces, `.sln`/project names, the add-in **`ClientId` GUID**,
  and the install/config paths — is stable for now. Changing it is a mechanical, wide
  refactor best done **once, deliberately, at the commercial re-baseline** — not piecemeal.
  The `ClientId` GUID must stay stable or it breaks every installed copy.

---

## Principles

- **One toolkit, one ribbon, one install.** Features are peers, not separate add-ins
  (this is why `KKimensions` folds in). The Project Comparator is the deliberate
  exception — split into its own tool in July 2026.
- **Never destabilize the user's model.** The Comparator (now its own tool) stays
  report-only until write-back is deliberately gated, per item, with confirmation.
- **Standardize the firm's *real* standards.** Harvest best-of-breed from live projects
  into the Standard; don't impose a stale master template.
- **Shippable-to-everyone is the quality bar** for reaching phase (b).

---

## Related docs

| Doc | Purpose |
|-----|---------|
| [`CLAUDE.md`](CLAUDE.md) | Build, architecture, threading, deploy — authoritative technical reference |
| [`README.md`](README.md) | Short public-facing overview + build/deploy quickstart |
| [`docs/BACKLOG.md`](docs/BACKLOG.md) | Toolkit-wide task list (links to per-tool backlogs) |
| `docs/tools/<tool>/` | Per-tool README, backlog, design specs and plans |
| `docs/toolkit/` | Toolkit-wide specs and plans |
| `docs/future/` | Specs for tools not yet built |
| `docs/archive/` | Retired historical docs |
