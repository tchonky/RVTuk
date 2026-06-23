# RVTuk.Core / Comparison

Pure-Core comparison logic for the Project Comparator (no Revit or WPF types). Placeholder folder — see [design spec §5](../../../docs/superpowers/specs/2026-06-23-project-comparator-design.md).

Planned contents:
- `ICategoryComparer.cs` — match + diff + score for one category.
- `ICategoryMerger.cs` — `AcceptIntoStandard(...)`; edits the Standard (data only, never Revit).
- `CategoryRegistry.cs` — registers comparers/mergers by `CategoryId`.
- `ComparisonEngine.cs` — category-agnostic orchestration → `ComparisonResult`.
- `ViewTemplateComparer.cs`, `ViewTemplateMerger.cs` — the v1 category.
- `StandardCurator.cs` — accept/conflict/provenance for the editable Standard.
- `FamiliesComparer.cs` — (phase 2) adapts the existing family index; no `.rfa` re-scan.

> Extraction lives in `RVTuk.Revit/Extraction` (`ICategoryExtractor`) because it touches the Revit API. Future Revit write-back lives in `RVTuk.Revit` (`ICategoryApplier`) — not in v1.
