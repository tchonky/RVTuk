# RVTuk.Core / Serialization

JSON helper for snapshot payloads, isolating the multi-target split. Placeholder folder — see [design spec §5.4](../../../docs/superpowers/specs/2026-06-23-project-comparator-design.md).

Planned contents:
- `SnapshotJson.cs` — `Serialize<T>` / `Deserialize<T>` with `#if REVIT2024` → `DataContractJsonSerializer` (net48, no `System.Text.Json` available — it clashes with Revit's preloaded assemblies, mirroring `Config/ConfigManager`), `#else` → `System.Text.Json` (net8).

Snapshot payloads are stored as JSON text columns in `.Setup\RVTuk.Standards.db` via `Database/SnapshotRepository.cs`, so the DB schema never changes when a new category is added.
