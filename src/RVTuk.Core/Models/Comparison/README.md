# RVTuk.Core / Models / Comparison

Serializable DTOs for the Project Comparator. All `[DataContract]`/`[DataMember]`-annotated so they round-trip on net48 (`DataContractJsonSerializer`) and net8 (`System.Text.Json`). Keep flat; use key/value lists, not raw `Dictionary`. Placeholder folder — see [design spec §5](../../../../docs/superpowers/specs/2026-06-23-project-comparator-design.md).

Planned contents:
- `CategorySnapshot.cs` (base), `ViewTemplatesSnapshot.cs`, `StandardSnapshot.cs` (mutable).
- `SnapshotMeta.cs` — SourceKind / Name / Path / RevitYear / CapturedUtc / SchemaVersion / IsMutable / Revision.
- `ComparisonResult.cs`, `CategoryDiffResult.cs`, `ItemDiff.cs`, `FieldDiff.cs`, `DiffKind.cs`.
- `ItemProvenance.cs` — which source snapshot each item/field came from.
- `DependencyClosure.cs` — filters → params (GUIDs) → patterns/subcategories that must travel.
- `MergeResult.cs` — outcome of `AcceptIntoStandard` (applied / conflict / closure gaps).
