# Auto Dimensions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a ribbon command that scans the active view for `"Dimensions_Line"` detail lines
and, for each, creates a Revit `Dimension` positioned exactly on the line, referencing every
wall that transversally crosses it — re-runnable after model changes without manual re-picking.

**Architecture:** A pure, unit-tested 2D crossing-detection function lives in `RVTuk.Core`
(plain `double` coordinates in, plain integer indices out — no Revit types, testable in
`tests\RVTuk.Core.Tests` without a live Revit session). `RVTuk.Revit` projects wall centerlines
and the reference line onto the view plane (dropping Z), calls the Core function, resolves each
crossing wall's two side faces via `HostObjectUtils.GetSideFaces`, and creates the `Dimension`
via `Document.Create.NewDimension`. Idempotent re-runs are handled by an Extensible Storage
entry on each reference line recording the `ElementId` of the dimension it last produced, so a
stale dimension is deleted (with view-ownership validation) before a fresh one is built. The
whole operation is one synchronous `IExternalCommand` + one `Transaction`, matching the existing
`SetupRishuiZaminParamsCommand` pattern — no `ExternalEvent` marshaling needed since Revit
already invokes command execution on the main thread.

**Tech Stack:** C#, Revit API (`Autodesk.Revit.DB.ExtensibleStorage`, `HostObjectUtils`,
`Categories.NewSubcategory`, `Document.Create.NewDimension`), xunit.

## Global Constraints

- `RVTuk.Core` must stay free of Revit API and WPF types (existing architecture rule in
  `CLAUDE.md`); only `RVTuk.Revit` may reference `Autodesk.Revit.*`.
- V1 scope only: `Wall` objects, active-view-only, single straight `DetailLine` reference lines,
  no editing of user-made dimensions. (See the design spec's Non-goals — do not build any of
  the deferred items.)
- A wall whose centerline is within **5°** of parallel to the reference line is excluded from
  that line's crossings (collinear and near-parallel/oblique cases both), per the revised spec.
- A wall only counts as crossing if the intersection point falls **strictly inside** both the
  wall's centerline segment and the reference line's bounded segment (excludes T-junctions/
  endpoint touches), per the revised spec.
- Only straight walls are considered — any `Wall` whose `LocationCurve.Curve` isn't a `Line` is
  filtered out before reaching the Core crossing logic.
- Deleting a stale tracked dimension must happen **unconditionally**, before checking whether
  the line currently has any crossings — this was a real bug in the first draft of the spec
  (caught by review) and must not be reintroduced.
- Before deleting a line's previously-tracked dimension, verify it still exists, is actually a
  `Dimension`, and its `OwnerViewId` equals the *active* view's id — guards against a reference
  line copied into another view deleting that other view's legitimate dimension.
- Build against both `Release2024` and `Release2025` configs before considering a task done.
- Branch: work happens on `Adimensions` (already checked out and pushed with tracking to
  `origin/Adimensions`).
- Full spec: `docs/superpowers/specs/2026-07-04-auto-dimensions-design.md`.

---

### Task 1: Core — XyPoint, WallCandidate, WallCrossingFinder

**Files:**
- Create: `src/RVTuk.Core/AutoDimensions/XyPoint.cs`
- Create: `src/RVTuk.Core/AutoDimensions/WallCandidate.cs`
- Create: `src/RVTuk.Core/AutoDimensions/WallCrossingFinder.cs`
- Test: `tests/RVTuk.Core.Tests/AutoDimensions/WallCrossingFinderTests.cs`

**Interfaces:**
- Produces: `readonly record struct XyPoint(double X, double Y)` — a plain 2D point, used by
  Task 4 (Revit side) to represent the reference line's endpoints and each wall's projected
  centerline endpoints.
- Produces: `record WallCandidate(XyPoint Start, XyPoint End)` — one candidate wall's projected
  centerline, consumed by Task 4 (Revit side builds a parallel list of `WallCandidate` alongside
  its own list of `Wall` elements, so a returned index maps back to the original `Wall`).
- Produces:
  `WallCrossingFinder.FindCrossingIndices(XyPoint lineStart, XyPoint lineEnd, IReadOnlyList<WallCandidate> walls) : IReadOnlyList<int>`
  — returns the indices (into the input `walls` list) of walls that transversally cross the
  line, ordered by increasing projection along the line (start → end). Consumed by Task 4.

**Algorithm (exact, so the tests in Step 1 are unambiguous):**
1. Treat the reference line as `P(t) = lineStart + t * (lineEnd - lineStart)`, `t` unbounded
   (computed for every wall, then checked against `(0, 1)` for the *bounded* segment).
2. Treat each wall as `Q(s) = wall.Start + s * (wall.End - wall.Start)`, same convention.
3. Compute the undirected angle between the line's direction and the wall's direction, folded
   into `[0, 90]` degrees (0 = parallel/collinear, 90 = perpendicular). If this angle is
   `< 5.0`, exclude the wall — regardless of where it would otherwise cross.
4. Otherwise, solve the standard 2D line-intersection equations for `t` and `s`. If
   `1e-6 < t < 1 - 1e-6` **and** `1e-6 < s < 1 - 1e-6`, the wall counts as crossing at
   parameter `t`; otherwise exclude it (this both keeps the crossing within each segment's
   bounds and rejects endpoint-touching T-junctions, since a pure endpoint touch always lands
   exactly at `s = 0` or `s = 1`, which the epsilon bounds reject).
5. Return the indices of all counted crossings, sorted by ascending `t`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class WallCrossingFinderTests
{
    // Reference line: horizontal, from (0,0) to (10,0).
    private static readonly XyPoint LineStart = new(0, 0);
    private static readonly XyPoint LineEnd = new(10, 0);

    [Fact]
    public void WallsCrossingInVariousOrders_AreSortedByProjectionAlongLine()
    {
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(8, -5), new XyPoint(8, 5)),  // crosses at t=0.8
            new(new XyPoint(2, -5), new XyPoint(2, 5)),  // crosses at t=0.2
            new(new XyPoint(5, -5), new XyPoint(5, 5)),  // crosses at t=0.5
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Equal(new[] { 1, 2, 0 }, result);
    }

    [Fact]
    public void WallTouchingOnlyAtEndpoint_IsExcluded()
    {
        // Wall's own start point (5,0) lies exactly on the reference line: a T-junction.
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(5, 0), new XyPoint(5, 5)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Empty(result);
    }

    [Fact]
    public void CollinearWall_IsExcluded()
    {
        // Wall runs along the same direction as the line (0 degrees from parallel).
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(3, 0), new XyPoint(7, 0)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Empty(result);
    }

    [Fact]
    public void ObliqueWallWithinTolerance_IsExcluded()
    {
        // ~3 degrees from parallel to the line (tan(3 deg) * 6 =~ 0.314).
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(2, -0.157), new XyPoint(8, 0.157)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Empty(result);
    }

    [Fact]
    public void ObliqueWallOutsideTolerance_IsIncluded()
    {
        // ~10 degrees from parallel to the line (tan(10 deg) * 6 =~ 1.058).
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(2, -0.529), new XyPoint(8, 0.529)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void WallNotCrossingLineAtAll_ReturnsEmpty()
    {
        // Both endpoints are above the line (y > 0); never reaches y=0.
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(2, 5), new XyPoint(8, 10)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Empty(result);
    }

    [Fact]
    public void WallCrossingBeyondLineBounds_IsExcluded()
    {
        // The infinite lines would cross at x=15, beyond the reference line's segment (x in [0,10]).
        var walls = new List<WallCandidate>
        {
            new(new XyPoint(15, -5), new XyPoint(15, 5)),
        };

        var result = WallCrossingFinder.FindCrossingIndices(LineStart, LineEnd, walls);

        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~WallCrossingFinderTests"`
Expected: FAIL to compile — `RVTuk.Core.AutoDimensions` namespace/types don't exist yet.

- [ ] **Step 3: Create the value types**

`src/RVTuk.Core/AutoDimensions/XyPoint.cs`:
```csharp
namespace RVTuk.Core.AutoDimensions
{
    /// <summary>A plain 2D point (view-plane projection — Z is deliberately not represented).</summary>
    public readonly record struct XyPoint(double X, double Y);
}
```

`src/RVTuk.Core/AutoDimensions/WallCandidate.cs`:
```csharp
namespace RVTuk.Core.AutoDimensions
{
    /// <summary>One wall's centerline, projected onto the view plane.</summary>
    public record WallCandidate(XyPoint Start, XyPoint End);
}
```

- [ ] **Step 4: Implement WallCrossingFinder**

`src/RVTuk.Core/AutoDimensions/WallCrossingFinder.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Finds which walls transversally cross a reference line, in 2D (view-plane) coordinates,
    /// excluding near-parallel/collinear walls and endpoint-only (T-junction) touches.
    /// </summary>
    public static class WallCrossingFinder
    {
        private const double ParallelToleranceDegrees = 5.0;
        private const double BoundaryEpsilon = 1e-6;

        public static IReadOnlyList<int> FindCrossingIndices(
            XyPoint lineStart, XyPoint lineEnd, IReadOnlyList<WallCandidate> walls)
        {
            var d1X = lineEnd.X - lineStart.X;
            var d1Y = lineEnd.Y - lineStart.Y;
            var lineAngle = Math.Atan2(d1Y, d1X);

            var crossings = new List<(int Index, double T)>();

            for (int i = 0; i < walls.Count; i++)
            {
                var wall = walls[i];
                var d2X = wall.End.X - wall.Start.X;
                var d2Y = wall.End.Y - wall.Start.Y;

                var wallAngle = Math.Atan2(d2Y, d2X);
                if (AngleFromParallelDegrees(lineAngle, wallAngle) < ParallelToleranceDegrees)
                    continue;

                var denom = d1X * d2Y - d1Y * d2X;
                if (Math.Abs(denom) < 1e-12) continue;

                var dx = wall.Start.X - lineStart.X;
                var dy = wall.Start.Y - lineStart.Y;

                var t = (dx * d2Y - dy * d2X) / denom;
                var s = (dx * d1Y - dy * d1X) / denom;

                if (t <= BoundaryEpsilon || t >= 1 - BoundaryEpsilon) continue;
                if (s <= BoundaryEpsilon || s >= 1 - BoundaryEpsilon) continue;

                crossings.Add((i, t));
            }

            return crossings.OrderBy(c => c.T).Select(c => c.Index).ToList();
        }

        /// <summary>0 = the two directions are parallel/collinear, 90 = perpendicular.</summary>
        private static double AngleFromParallelDegrees(double angleA, double angleB)
        {
            var diffDegrees = Math.Abs(angleA - angleB) * 180.0 / Math.PI;
            diffDegrees %= 180.0;
            if (diffDegrees > 90.0) diffDegrees = 180.0 - diffDegrees;
            return diffDegrees;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FullyQualifiedName~WallCrossingFinderTests"`
Expected: PASS (7 tests).

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Core/AutoDimensions tests/RVTuk.Core.Tests/AutoDimensions
git commit -m "feat(auto-dimensions): add 2D wall-crossing detection (Core, unit-tested)"
```

---

### Task 2: Revit — DimensionLineStyle

**Files:**
- Create: `src/RVTuk.Revit/AutoDimensions/DimensionLineStyle.cs`

**Interfaces:**
- Produces: `DimensionLineStyle.LineStyleName : string` (constant, `"Dimensions_Line"`),
  `DimensionLineStyle.EnsureExists(Document doc) : void`, and
  `DimensionLineStyle.IsDimensionsLine(CurveElement curveElement) : bool` — all consumed by
  Task 4 (`AutoDimensionsCommand`).

- [ ] **Step 1: Implement it**

```csharp
using Autodesk.Revit.DB;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// The dedicated line subcategory ("Dimensions_Line") that marks a detail line as a
    /// reference for Auto Dimensions. Auto-creates on first use — no separate setup step.
    /// </summary>
    public static class DimensionLineStyle
    {
        public const string LineStyleName = "Dimensions_Line";

        public static void EnsureExists(Document doc)
        {
            var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCategory.SubCategories.Contains(LineStyleName)) return;
            doc.Settings.Categories.NewSubcategory(linesCategory, LineStyleName);
        }

        public static bool IsDimensionsLine(CurveElement curveElement)
        {
            return curveElement.LineStyle is GraphicsStyle style
                && style.GraphicsStyleCategory != null
                && style.GraphicsStyleCategory.Name == LineStyleName;
        }
    }
}
```

- [ ] **Step 2: Build RVTuk.Revit to verify it compiles**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions/DimensionLineStyle.cs
git commit -m "feat(auto-dimensions): add DimensionLineStyle (auto-creating line subcategory)"
```

---

### Task 3: Revit — AutoDimensionTracker

**Files:**
- Create: `src/RVTuk.Revit/AutoDimensions/AutoDimensionTracker.cs`

**Interfaces:**
- Produces:
  `AutoDimensionTracker.TryGetTrackedDimension(Element line, ElementId activeViewId) : Dimension?`
  and `AutoDimensionTracker.SetTrackedDimension(Element line, ElementId dimensionId) : void` —
  both consumed by Task 4 (`AutoDimensionsCommand`).

- [ ] **Step 1: Implement it**

```csharp
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Tracks, per reference line, the ElementId of the Dimension it last produced — via
    /// Extensible Storage (invisible to the user, travels with the line through copy/move).
    /// Validates the tracked dimension still exists, is a Dimension, and belongs to the active
    /// view before ever handing it back for deletion (guards a line copied to another view from
    /// deleting that other view's legitimate dimension).
    /// </summary>
    public static class AutoDimensionTracker
    {
        private static readonly Guid SchemaGuid = new Guid("2f1c9b6e-6b7d-4a6d-9c9a-8e6a1f6d9b2a");
        private const string SchemaName = "RVTukAutoDimensionTracking";
        private const string FieldName = "DimensionIdValue";

        public static Dimension? TryGetTrackedDimension(Element line, ElementId activeViewId)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;

            var entity = line.GetEntity(schema);
            if (!entity.IsValid()) return null;

            var idValue = entity.Get<long>(FieldName);
            var elementId = new ElementId(idValue);

            if (line.Document.GetElement(elementId) is not Dimension dimension) return null;
            if (dimension.OwnerViewId != activeViewId) return null;

            return dimension;
        }

        public static void SetTrackedDimension(Element line, ElementId dimensionId)
        {
            var schema = GetOrCreateSchema();
            var entity = new Entity(schema);
            entity.Set(FieldName, dimensionId.Value);
            line.SetEntity(entity);
        }

        private static Schema GetOrCreateSchema()
        {
            var existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetVendorId("KnafoKlimor");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldName, typeof(long));
            return builder.Finish();
        }
    }
}
```

- [ ] **Step 2: Build RVTuk.Revit to verify it compiles**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions/AutoDimensionTracker.cs
git commit -m "feat(auto-dimensions): add AutoDimensionTracker (Extensible Storage bookkeeping)"
```

---

### Task 4: Revit — AutoDimensionsCommand + ribbon button + Application.cs wiring

**Files:**
- Create: `src/RVTuk.Revit/AutoDimensions/AutoDimensionsCommand.cs`
- Modify: `src/RVTuk.Revit/Application.cs`

**Interfaces:**
- Consumes: `WallCrossingFinder.FindCrossingIndices`, `XyPoint`, `WallCandidate` (Task 1),
  `DimensionLineStyle.EnsureExists` / `.IsDimensionsLine` (Task 2),
  `AutoDimensionTracker.TryGetTrackedDimension` / `.SetTrackedDimension` (Task 3).
- Produces: registered ribbon button; nothing further consumes this — it's the orchestration
  and wiring task.

- [ ] **Step 1: Implement the command**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    [Transaction(TransactionMode.Manual)]
    public class AutoDimensionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var doc = uiDoc.Document;
            var view = uiDoc.ActiveView;

            var created = 0;
            var skipped = 0;

            using (var tx = new Transaction(doc, "Auto Dimensions"))
            {
                tx.Start();

                DimensionLineStyle.EnsureExists(doc);

                var referenceLines = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(DetailLine))
                    .Cast<DetailLine>()
                    .Where(DimensionLineStyle.IsDimensionsLine)
                    .ToList();

                var straightWalls = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .Where(w => (w.Location as LocationCurve)?.Curve is Line)
                    .ToList();

                foreach (var line in referenceLines)
                {
                    var tracked = AutoDimensionTracker.TryGetTrackedDimension(line, view.Id);
                    if (tracked != null)
                    {
                        doc.Delete(tracked.Id);
                    }

                    if (line.GeometryCurve is not Line geometryLine)
                    {
                        skipped++;
                        continue;
                    }

                    var lineStart = ToXyPoint(geometryLine.GetEndPoint(0));
                    var lineEnd = ToXyPoint(geometryLine.GetEndPoint(1));

                    var candidates = straightWalls
                        .Select(w => (Line)((LocationCurve)w.Location).Curve)
                        .Select(c => new WallCandidate(ToXyPoint(c.GetEndPoint(0)), ToXyPoint(c.GetEndPoint(1))))
                        .ToList();

                    var crossingIndices = WallCrossingFinder.FindCrossingIndices(lineStart, lineEnd, candidates);
                    if (crossingIndices.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    var referenceArray = new ReferenceArray();
                    var anyFaceFound = false;
                    foreach (var index in crossingIndices)
                    {
                        var wall = straightWalls[index];
                        var exteriorFaces = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
                        var interiorFaces = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);
                        if (exteriorFaces.Count == 0 || interiorFaces.Count == 0) continue;

                        referenceArray.Append(exteriorFaces[0]);
                        referenceArray.Append(interiorFaces[0]);
                        anyFaceFound = true;
                    }

                    if (!anyFaceFound)
                    {
                        skipped++;
                        continue;
                    }

                    var dimension = doc.Create.NewDimension(view, geometryLine, referenceArray);
                    AutoDimensionTracker.SetTrackedDimension(line, dimension.Id);
                    created++;
                }

                tx.Commit();
            }

            var summary = new StringBuilder();
            if (created == 0 && skipped == 0)
            {
                summary.Append("No Dimensions_Line lines found — the line style now exists in " +
                    "this project; draw reference lines and run again.");
            }
            else
            {
                summary.Append($"{created} dimension(s) created, {skipped} line(s) skipped " +
                    "(no walls found).");
            }
            TaskDialog.Show("RVTuk – Auto Dimensions", summary.ToString());

            return Result.Succeeded;
        }

        private static XyPoint ToXyPoint(XYZ point) => new(point.X, point.Y);
    }
}
```

- [ ] **Step 2: Add the ribbon button and icon in Application.cs**

Add `using RVTuk.Revit.AutoDimensions;` to the top of `src/RVTuk.Revit/Application.cs`, alongside
the existing `using RVTuk.Revit.NeoProperties;` (if present) or `using RVTuk.Revit.Commands;`.

In `CreateRibbon`, after the `Neo Properties` panel block added previously (or after the
`areaBtn`/`panel.AddItem(areaBtn);` block if Neo Properties hasn't been merged into this branch
yet), add:
```csharp
            RibbonPanel autoDimPanel = app.CreateRibbonPanel("Auto Dimensions");
            var autoDimBtn = new PushButtonData(
                "AutoDimensions",
                "Auto\nDimensions",
                assemblyPath,
                typeof(AutoDimensionsCommand).FullName!)
            {
                ToolTip = "Dimension every wall crossing a Dimensions_Line detail line in the active view"
            };
            autoDimBtn.LargeImage = CreateAutoDimensionsIcon(32);
            autoDimBtn.Image      = CreateAutoDimensionsIcon(16);

            autoDimPanel.AddItem(autoDimBtn);
```

Add the icon method alongside the other `Create...Icon` methods (e.g. after `CreateAreaCalcIcon`):
```csharp
        private static BitmapSource CreateAutoDimensionsIcon(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double s = size;
                ctx.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x25, 0x25, 0x26)), null,
                    new Rect(0, 0, s, s));

                var pen = new Pen(new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x8C, 0x00)), Math.Max(1, s * 0.06));
                pen.Freeze();
                double y = s * 0.5;
                // Dimension line with tick marks at each end and one in the middle.
                ctx.DrawLine(pen, new WpfPoint(s * 0.14, y), new WpfPoint(s * 0.86, y));
                foreach (var x in new[] { s * 0.14, s * 0.5, s * 0.86 })
                {
                    ctx.DrawLine(pen, new WpfPoint(x, y - s * 0.16), new WpfPoint(x, y + s * 0.16));
                }
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
```

- [ ] **Step 3: Build both configs to verify everything compiles**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/RVTuk.Revit/AutoDimensions/AutoDimensionsCommand.cs src/RVTuk.Revit/Application.cs
git commit -m "feat(auto-dimensions): add AutoDimensionsCommand, wire ribbon button"
```

---

### Task 5: Full build + CLAUDE.md + manual in-Revit verification

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Run the full Core test suite**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`
Expected: all tests pass, including the 7 new `WallCrossingFinderTests`.

- [ ] **Step 2: Build the whole solution, both configs**

Run: `dotnet build RVTuk.sln -c Release2024`
Expected: `Build succeeded`.

Run: `dotnet build RVTuk.sln -c Release2025`
Expected: `Build succeeded`.

- [ ] **Step 3: Update CLAUDE.md's feature list**

In `CLAUDE.md`, under "Current and planned features," add a line after the last existing
feature bullet:
```markdown
- **Auto Dimensions** — draw a detail line on the dedicated "Dimensions_Line" style as a
  positional reference; a ribbon command dimensions every wall crossing it, re-runnable after
  model changes without re-picking references. See
  [`docs/superpowers/specs/2026-07-04-auto-dimensions-design.md`](docs/superpowers/specs/2026-07-04-auto-dimensions-design.md).
```

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add Auto Dimensions to CLAUDE.md feature list"
```

- [ ] **Step 5: Deploy and manually verify in Revit**

Run (elevated shell, from repo root): `.\Deploy.ps1`

Then in Revit, following the design spec's manual test plan:
1. Confirm the "Auto Dimensions" button appears in its own panel on the RVTuk ribbon tab.
2. In a project without the `Dimensions_Line` line style yet, run the command with no reference
   lines drawn — confirm it succeeds, creates the line style, and reports "No Dimensions_Line
   lines found…".
3. Draw a `Dimensions_Line` detail line crossing 2–3 walls; run; confirm one dimension appears
   exactly on the line, referencing the correct wall faces in order along the line.
4. Move one of the crossed walls; re-run; confirm the old dimension is gone and a new,
   correctly-updated one appears (no duplicate).
5. Delete (or move away) all the walls a line previously crossed, then re-run; confirm the
   stale dimension is removed and the line is tallied as skipped.
6. Draw a `Dimensions_Line` crossing zero walls; run; confirm no dimension is created and it's
   tallied as skipped.
7. Run with multiple reference lines in the same view; confirm each gets its own correct
   dimension and the summary tallies all of them.
8. Copy a reference line (with its tracked dimension) into a different view; run the command in
   that new view; confirm it creates its own dimension there and does not delete the dimension
   still owned by the original view.
9. Manually delete a dimension the tool previously created, leaving its line's tracking entry
   dangling; re-run; confirm no error, and a fresh dimension is created and tracked normally.

---

## Deferred / explicitly out of scope

Per the design spec's non-goals — do not implement in this plan:
- Object types other than Walls (columns, grids, openings, etc.).
- Processing every view in the project in one run.
- Multi-segment/chained reference lines.
- Editing existing manually-placed dimensions.
- A visible/editable parameter for the tracking mechanism.
- Curved (arc/spline) walls.
- Reverse tracking from a dimension back to its originating line.
