using System;
using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.Shared.Geometry;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class OutlineCornerPlannerTests
{
    // One millimetre in Revit's internal feet.
    private const double Mm = 1.0 / 304.8;

    private static readonly XyzPoint[] None = Array.Empty<XyzPoint>();

    private static readonly CornerRecord[] NoRecords = Array.Empty<CornerRecord>();

    // A 10 × 10 ft outline with a 2 × 2 hole in its middle.
    private static readonly IReadOnlyList<IReadOnlyList<XyPoint>> SquareWithHole = new[]
    {
        new[] { new XyPoint(0, 0), new XyPoint(10, 0), new XyPoint(10, 10), new XyPoint(0, 10) },
        new[] { new XyPoint(4, 4), new XyPoint(6, 4), new XyPoint(6, 6), new XyPoint(4, 6) },
    };

    private static readonly IReadOnlyList<XyPoint> SquareWithHoleCorners = SquareWithHole.SelectMany(loop => loop).ToList();

    private static Func<XyzPoint, double?> Always(double? z) => _ => z;

    private static IEnumerable<T[]> Orders<T>(IReadOnlyList<T> items)
    {
        if (items.Count <= 1)
        {
            yield return items.ToArray();
            yield break;
        }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, j) => j != i).ToArray();
            foreach (var tail in Orders(rest)) yield return new[] { items[i] }.Concat(tail).ToArray();
        }
    }

    private static void AssertSamePlan(CornerPlan expected, CornerPlan actual)
    {
        Assert.Equal(expected.Raises, actual.Raises);
        Assert.Equal(expected.PutBacks, actual.PutBacks);
        Assert.Equal(expected.NewRecords, actual.NewRecords);
        Assert.Equal(expected.NewlyByHand, actual.NewlyByHand);
    }

    // ---------------------------------------------------------------- Candidates

    [Fact]
    public void OutlineAndHoleVerticesAreCandidates()
    {
        var vertices = new[]
        {
            new XyzPoint(0, 0, 10), new XyzPoint(10, 10, 10), new XyzPoint(4, 6, 10),
            new XyzPoint(5, 0, 10), // on an edge, not a corner
        };

        var candidates = OutlineCornerPlanner.Candidates(vertices, SquareWithHole, None, None);

        Assert.Equal(vertices, candidates);
    }

    [Fact]
    public void AVertexTwoMillimetresInsideTheOutlineIsNotACandidate()
    {
        var inside = new XyzPoint(2 * Mm, 5, 10);
        var justInside = new XyzPoint(0.9 * Mm, 5, 10);

        var candidates = OutlineCornerPlanner.Candidates(
            new[] { inside, justInside, new XyzPoint(2, 2, 10) }, SquareWithHole, None, None);

        Assert.Equal(new[] { justInside }, candidates);
    }

    [Fact]
    public void TheEdgeBandIsOneMillimetre()
    {
        var at09 = new XyzPoint(0.9 * Mm, 5, 10);
        var at11 = new XyzPoint(1.1 * Mm, 5, 10);
        var holeAt09 = new XyzPoint(5, 6 - 0.9 * Mm, 10);
        var holeAt11 = new XyzPoint(5, 6 - 1.1 * Mm, 10);

        var candidates = OutlineCornerPlanner.Candidates(
            new[] { at09, at11, holeAt09, holeAt11 }, SquareWithHole, None, None);

        Assert.Equal(new[] { at09, holeAt09 }, candidates);
    }

    [Fact]
    public void ThePlacedPointBandIsOneMillimetre()
    {
        var vertex = new XyzPoint(5, 0, 10);

        Assert.Empty(OutlineCornerPlanner.Candidates(
            new[] { vertex }, SquareWithHole, None, new[] { new XyzPoint(5 + 0.9 * Mm, 0, 10) }));
        Assert.Single(OutlineCornerPlanner.Candidates(
            new[] { vertex }, SquareWithHole, None, new[] { new XyzPoint(5 + 1.1 * Mm, 0, 10) }));
    }

    [Fact]
    public void AVertexTheLedgerRecordsInXYAndZIsNotACandidate()
    {
        var recorded = new XyzPoint(0, 0, 10);
        var sameSpotOtherHeight = new XyzPoint(10, 0, 10.2); // Z off by far more than 0.1 mm
        var ledgered = new[]
        {
            new XyzPoint(0.09 * Mm, -0.09 * Mm, 10 + 0.09 * Mm),
            new XyzPoint(10, 0, 10),
        };

        var candidates = OutlineCornerPlanner.Candidates(
            new[] { recorded, sameSpotOtherHeight }, SquareWithHole, ledgered, None);

        Assert.Equal(new[] { sameSpotOtherHeight }, candidates);
    }

    [Fact]
    public void LedgerMatchUsesATenthOfAMillimetreOnEachAxis()
    {
        var vertex = new XyzPoint(0, 0, 10);

        Assert.Single(OutlineCornerPlanner.Candidates(
            new[] { vertex }, SquareWithHole, new[] { new XyzPoint(0.11 * Mm, 0, 10) }, None));
        Assert.Single(OutlineCornerPlanner.Candidates(
            new[] { vertex }, SquareWithHole, new[] { new XyzPoint(0, 0.11 * Mm, 10) }, None));
        Assert.Single(OutlineCornerPlanner.Candidates(
            new[] { vertex }, SquareWithHole, new[] { new XyzPoint(0, 0, 10 + 0.11 * Mm) }, None));
    }

    [Fact]
    public void AVertexWithinOneMillimetreInPlanOfAPlacedPointIsNotACandidate()
    {
        var vertex = new XyzPoint(5, 0, 10);
        var near = new XyzPoint(5 + 0.6 * Mm, 0.6 * Mm, 99); // 0.85 mm away in plan, any Z
        var far = new XyzPoint(5 + 0.8 * Mm, 0.8 * Mm, 10); // 1.13 mm away in plan

        Assert.Empty(OutlineCornerPlanner.Candidates(new[] { vertex }, SquareWithHole, None, new[] { near }));
        Assert.Single(OutlineCornerPlanner.Candidates(new[] { vertex }, SquareWithHole, None, new[] { far }));
    }

    [Fact]
    public void AVertexHalfAMillimetreFromALedgeredSpotAtAnotherHeightIsACandidateOnlyIfNoPlacedPointIsNear()
    {
        // Review Focus 1: the corner sits on a line's end. The line point is ledgered at its own
        // spot and height; the corner 0.5 mm away at another height is not that point.
        var ledgeredPoint = new XyzPoint(10, 10, 12);
        var corner = new XyzPoint(10 - 0.5 * Mm, 10, 10);

        Assert.Equal(new[] { corner }, OutlineCornerPlanner.Candidates(
            new[] { corner }, SquareWithHole, new[] { ledgeredPoint }, None));
        Assert.Empty(OutlineCornerPlanner.Candidates(
            new[] { corner }, SquareWithHole, new[] { ledgeredPoint }, new[] { ledgeredPoint }));
    }

    // ---------------------------------------------------------------- Plan

    [Fact]
    public void ARecordFlaggedByHandIsKeptAndLeftAlone()
    {
        var corner = new XyzPoint(0, 0, 13);
        var record = new CornerRecord(0, 0, 10, 12, true);
        bool asked = false;

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, _ => { asked = true; return 20; }, new[] { record });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(new[] { record }, plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
        Assert.False(asked);
    }

    [Fact]
    public void ARecordWhoseCornerMovedOffItsSetHeightIsFlaggedByHand()
    {
        var corner = new XyzPoint(0, 0, 12 + 0.11 * Mm);
        var record = new CornerRecord(0, 0, 10, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(20), new[] { record });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(new[] { record with { ByHand = true } }, plan.NewRecords);
        Assert.Equal(1, plan.NewlyByHand);
    }

    [Fact]
    public void ACornerWithinATenthOfAMillimetreOfItsSetHeightIsNotFlagged()
    {
        var corner = new XyzPoint(0, 0, 12 + 0.09 * Mm);
        var record = new CornerRecord(0, 0, 10, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(20), new[] { record });

        Assert.Equal(0, plan.NewlyByHand);
        Assert.Equal(new[] { (corner, 20.0) }, plan.Raises);
    }

    [Fact]
    public void ANewCornerWithATargetIsRaisedAndRecordedWithItsOwnHeight()
    {
        var corner = new XyzPoint(0, 0, 10);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(12), NoRecords);

        Assert.Equal(new[] { (corner, 12.0) }, plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(new[] { new CornerRecord(0, 0, 10, 12, false) }, plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
    }

    [Fact]
    public void ACornerAlreadyWithinATenthOfAMillimetreOfItsTargetIsRecordedWhereItIsNotRaised()
    {
        var corner = new XyzPoint(0, 0, 10);
        double target = 10 + 0.09 * Mm;

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(target), NoRecords);

        Assert.Empty(plan.Raises);
        // SetZ is where the corner is, not the target: a re-run must not rewrite the record.
        Assert.Equal(new[] { new CornerRecord(0, 0, 10, 10, false) }, plan.NewRecords);

        var justOver = OutlineCornerPlanner.Plan(new[] { corner }, Always(10 + 0.11 * Mm), NoRecords);
        Assert.Single(justOver.Raises);
    }

    [Fact]
    public void ARecordedCornerWithATargetKeepsItsFirstOwnHeight()
    {
        var corner = new XyzPoint(0, 0, 12);
        var record = new CornerRecord(0, 0, 10, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(14), new[] { record });

        Assert.Equal(new[] { (corner, 14.0) }, plan.Raises);
        Assert.Equal(new[] { new CornerRecord(0, 0, 10, 14, false) }, plan.NewRecords);
    }

    [Fact]
    public void ACornerRaisedTwiceKeepsTheFirstOwnHeight()
    {
        var corner = new XyzPoint(0, 0, 10);

        var first = OutlineCornerPlanner.Plan(new[] { corner }, Always(12), NoRecords);
        var second = OutlineCornerPlanner.Plan(
            new[] { corner with { Z = first.Raises[0].ToZ } }, Always(14), first.NewRecords);

        Assert.Equal(14.0, second.Raises.Single().ToZ);
        Assert.Equal(new[] { new CornerRecord(0, 0, 10, 14, false) }, second.NewRecords);
    }

    [Fact]
    public void ARecordedCornerLeftAloneGoesBackToItsOwnHeightAndLosesItsRecord()
    {
        var corner = new XyzPoint(0, 0, 12);
        var record = new CornerRecord(0, 0, 10, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(null), new[] { record });

        Assert.Empty(plan.Raises);
        Assert.Equal(new[] { (corner, 10.0) }, plan.PutBacks);
        Assert.Empty(plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
    }

    [Fact]
    public void ARecordedCornerLeftAloneAlreadyAtItsOwnHeightOnlyLosesItsRecord()
    {
        double own = 12 + 0.09 * Mm;
        var corner = new XyzPoint(0, 0, 12);
        var record = new CornerRecord(0, 0, own, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(null), new[] { record });

        Assert.Empty(plan.PutBacks);
        Assert.Empty(plan.NewRecords);
    }

    [Fact]
    public void ACornerLeftAloneWithoutARecordIsNotTouched()
    {
        var plan = OutlineCornerPlanner.Plan(new[] { new XyzPoint(0, 0, 10) }, Always(null), NoRecords);

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Empty(plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
    }

    [Fact]
    public void ARecordWithNoCandidateAtItsSpotIsDropped()
    {
        var plan = OutlineCornerPlanner.Plan(
            new[] { new XyzPoint(0, 0, 10) }, Always(null),
            new[] { new CornerRecord(5, 5, 10, 12, false), new CornerRecord(6, 6, 10, 12, true) });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Empty(plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
    }

    [Fact]
    public void RecordsPairWithCandidatesWithinATenthOfAMillimetreInPlan()
    {
        // A record stores its corner's live XY exactly, so 0.1 mm is ample.
        var corner = new XyzPoint(0, 0, 12);
        var near = new CornerRecord(0.06 * Mm, 0.06 * Mm, 10, 12, false); // 0.085 mm
        var far = new CornerRecord(0.08 * Mm, 0.08 * Mm, 10, 12, false); // 0.113 mm

        var matched = OutlineCornerPlanner.Plan(new[] { corner }, Always(null), new[] { near });
        Assert.Equal(new[] { (corner, 10.0) }, matched.PutBacks);

        var unmatched = OutlineCornerPlanner.Plan(new[] { corner }, Always(null), new[] { far });
        Assert.Empty(unmatched.PutBacks);
        Assert.Empty(unmatched.NewRecords);
    }

    [Fact]
    public void TheNearestRecordWins()
    {
        var corner = new XyzPoint(0, 0, 12);
        var nearer = new CornerRecord(0.03 * Mm, 0, 10, 12, false);
        var farther = new CornerRecord(0, 0.08 * Mm, 11, 12, false);

        foreach (var records in Orders(new[] { farther, nearer }))
        {
            var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(null), records);
            Assert.Equal(new[] { (corner, 10.0) }, plan.PutBacks);
        }
    }

    [Fact]
    public void ACornerMovedByHandAndLeftAloneIsFlaggedNotPutBack()
    {
        var corner = new XyzPoint(0, 0, 15);
        var record = new CornerRecord(0, 0, 10, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(null), new[] { record });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(new[] { record with { ByHand = true } }, plan.NewRecords);
        Assert.Equal(1, plan.NewlyByHand);
    }

    [Fact]
    public void TheRecordFollowsTheLiveCornersXy()
    {
        var corner = new XyzPoint(1, 2, 12);
        var record = new CornerRecord(1 + 0.05 * Mm, 2, 10, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(14), new[] { record });

        Assert.Equal(new[] { (corner, 14.0) }, plan.Raises);
        Assert.Equal(new[] { new CornerRecord(1, 2, 10, 14, false) }, plan.NewRecords);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANonFiniteTargetCountsAsLeftAlone(double bad)
    {
        var recorded = new XyzPoint(0, 0, 12);
        var unrecorded = new XyzPoint(10, 0, 10);

        var plan = OutlineCornerPlanner.Plan(
            new[] { recorded, unrecorded }, Always(bad), new[] { new CornerRecord(0, 0, 10, 12, false) });

        Assert.Empty(plan.Raises);
        Assert.Equal(new[] { (recorded, 10.0) }, plan.PutBacks);
        Assert.Empty(plan.NewRecords);
    }

    [Fact]
    public void EveryListIsSortedByXThenY()
    {
        var candidates = new[]
        {
            new XyzPoint(10, 10, 10), new XyzPoint(0, 10, 10), new XyzPoint(10, 0, 10), new XyzPoint(0, 0, 10),
            new XyzPoint(5, 5, 12), new XyzPoint(5, 1, 12),
        };
        var records = new[] { new CornerRecord(5, 5, 11, 12, false), new CornerRecord(5, 1, 11, 12, false) };
        Func<XyzPoint, double?> target = p => p.X == 5 ? null : 20;

        var plan = OutlineCornerPlanner.Plan(candidates, target, records);

        Assert.Equal(new[] { (0.0, 0.0), (0.0, 10.0), (10.0, 0.0), (10.0, 10.0) },
            plan.Raises.Select(m => (m.At.X, m.At.Y)));
        Assert.Equal(new[] { (5.0, 1.0), (5.0, 5.0) }, plan.PutBacks.Select(m => (m.At.X, m.At.Y)));
        Assert.Equal(new[] { (0.0, 0.0), (0.0, 10.0), (10.0, 0.0), (10.0, 10.0) },
            plan.NewRecords.Select(r => (r.X, r.Y)));
    }

    // Reviewers' scene (a): two outline vertices 0.9 mm apart, only B recorded.
    [Fact]
    public void TwoCornersNearOneAnotherEachKeepTheirOwnRecordInEveryOrder()
    {
        var a = new XyzPoint(0, 0, 10);
        var b = new XyzPoint(0.9 * Mm, 0, 12);
        var records = new[] { new CornerRecord(0.9 * Mm, 0, 9, 12, false) };

        var expected = new CornerPlan(
            new[] { (a, 12.0) },
            Array.Empty<(XyzPoint, double)>(),
            new[] { new CornerRecord(0, 0, 10, 12, false), new CornerRecord(0.9 * Mm, 0, 9, 12, false) },
            0);

        foreach (var candidates in Orders(new[] { a, b }))
            AssertSamePlan(expected, OutlineCornerPlanner.Plan(candidates, Always(12), records));
    }

    // Reviewers' scene (b): a record halfway between two corners belongs to neither.
    [Fact]
    public void ARecordBetweenTwoCornersIsDroppedInEveryOrder()
    {
        var a = new XyzPoint(0, 0, 12);
        var b = new XyzPoint(1.5 * Mm, 0, 10);
        var records = new[] { new CornerRecord(0.75 * Mm, 0, 10, 12, false) };

        var expected = new CornerPlan(
            Array.Empty<(XyzPoint, double)>(), Array.Empty<(XyzPoint, double)>(), NoRecords, 0);

        foreach (var candidates in Orders(new[] { a, b }))
            AssertSamePlan(expected, OutlineCornerPlanner.Plan(candidates, Always(null), records));
    }

    // Reviewers' scene (c): one corner, two records either side of it.
    [Fact]
    public void RecordsHalfAMillimetreEitherSideOfACornerAreNotItsInEveryOrder()
    {
        var corner = new XyzPoint(0, 0, 12);
        var records = new[]
        {
            new CornerRecord(-0.5 * Mm, 0, 10, 12, false), new CornerRecord(0.5 * Mm, 0, 11, 12, false),
        };

        var expected = new CornerPlan(
            new[] { (corner, 14.0) },
            Array.Empty<(XyzPoint, double)>(),
            new[] { new CornerRecord(0, 0, 12, 14, false) },
            0);

        foreach (var order in Orders(records))
            AssertSamePlan(expected, OutlineCornerPlanner.Plan(new[] { corner }, Always(14), order));
    }

    [Fact]
    public void RecordsEquallyNearACornerPairByTheRecordsXInEveryOrder()
    {
        var corner = new XyzPoint(0, 0, 12);
        var records = new[]
        {
            new CornerRecord(0.05 * Mm, 0, 11, 12, false), new CornerRecord(-0.05 * Mm, 0, 10, 12, false),
        };

        var expected = new CornerPlan(
            new[] { (corner, 14.0) },
            Array.Empty<(XyzPoint, double)>(),
            new[] { new CornerRecord(0, 0, 10, 14, false) },
            0);

        foreach (var order in Orders(records))
            AssertSamePlan(expected, OutlineCornerPlanner.Plan(new[] { corner }, Always(14), order));
    }

    // ---------------------------------------------------------------- WithReached

    [Theory]
    [InlineData(0.05 / 304.8)] // lands 0.05 mm above the target
    [InlineData(1e-12)]        // float noise, in feet
    [InlineData(-0.05 / 304.8)]
    public void ACornerThatLandsNearItsTargetKeepsAnIdenticalRecordOnTheNextRun(double landingError)
    {
        var candidates = new[] { new XyzPoint(0, 0, 10), new XyzPoint(10, 0, 11) };
        var records = new[] { new CornerRecord(10, 0, 9, 11, false) };
        Func<XyzPoint, double?> target = p => p.X == 0 ? 12 : 13;

        var first = OutlineCornerPlanner.Plan(candidates, target, records);
        Assert.Equal(2, first.Raises.Count);
        var reached = first.Raises.ToDictionary(m => (m.At.X, m.At.Y), m => m.ToZ + landingError);
        var stored = first.WithReached(reached);
        var landed = candidates.Select(c => c with { Z = reached[(c.X, c.Y)] }).ToArray();

        var second = OutlineCornerPlanner.Plan(landed, target, stored.NewRecords);

        Assert.Empty(second.Raises);
        Assert.Empty(second.PutBacks);
        Assert.Equal(0, second.NewlyByHand);
        Assert.Equal(stored.NewRecords, second.NewRecords);
        Assert.Equal(OutlineCornerRecordCodec.Encode(stored.NewRecords),
            OutlineCornerRecordCodec.Encode(second.NewRecords));
    }

    [Fact]
    public void WithReachedThrowsNamingARaisedCornerItHasNoHeightFor()
    {
        var plan = OutlineCornerPlanner.Plan(
            new[] { new XyzPoint(0, 0, 10), new XyzPoint(1.5, 2.25, 10) }, Always(12), NoRecords);

        var error = Assert.Throws<ArgumentException>(() => plan.WithReached(
            new Dictionary<(double X, double Y), double> { [(0, 0)] = 12 }));

        Assert.Contains("X = 1.5, Y = 2.25", error.Message);
        Assert.Equal("reachedZByXy", error.ParamName);
    }

    [Fact]
    public void WithReachedReplacesSetZByTheHeightTheCornerReached()
    {
        var raised = new XyzPoint(0, 0, 10);
        var handled = new XyzPoint(10, 0, 15);
        var untouched = new XyzPoint(10, 10, 10);
        var records = new[] { new CornerRecord(10, 0, 9, 12, false) };

        var plan = OutlineCornerPlanner.Plan(new[] { raised, handled, untouched },
            p => p.Y == 10 ? 10 : 12, records);

        var reached = plan.WithReached(new Dictionary<(double X, double Y), double>
        {
            [(0, 0)] = 11.9,
            [(10, 0)] = 99, // flagged by hand: left as it was
            [(10, 10)] = 50, // not raised: ignored
        });

        Assert.Equal(new[]
        {
            new CornerRecord(0, 0, 10, 11.9, false),
            new CornerRecord(10, 0, 9, 12, true),
            new CornerRecord(10, 10, 10, 10, false),
        }, reached.NewRecords);
        Assert.Same(plan.Raises, reached.Raises);
        Assert.Same(plan.PutBacks, reached.PutBacks);
        Assert.Equal(plan.NewlyByHand, reached.NewlyByHand);
    }

    [Fact]
    public void AReRunAfterApplyingThePlanChangesNothing()
    {
        var candidates = new[]
        {
            new XyzPoint(0, 0, 10),   // new, raised
            new XyzPoint(10, 0, 12),  // recorded, raised again
            new XyzPoint(10, 10, 12), // recorded, left alone: put back
            new XyzPoint(0, 10, 15),  // recorded, moved by hand
            new XyzPoint(5, 0, 10),   // left alone, no record
        };
        var records = new[]
        {
            new CornerRecord(10, 0, 9, 12, false),
            new CornerRecord(10, 10, 11, 12, false),
            new CornerRecord(0, 10, 10, 12, false),
            new CornerRecord(3, 3, 10, 12, false), // vertex gone
        };
        Func<XyzPoint, double?> target = p =>
            p.X == 0 && p.Y == 0 ? 11 : p.X == 10 && p.Y == 0 ? 13 : p.Y == 10 && p.X == 0 ? 16 : null;

        var first = OutlineCornerPlanner.Plan(candidates, target, records);
        Assert.Equal(2, first.Raises.Count);
        Assert.Single(first.PutBacks);
        Assert.Equal(1, first.NewlyByHand);

        var moves = first.Raises.Concat(first.PutBacks).ToList();
        var applied = candidates
            .Select(c => moves.Where(m => m.At == c).Select(m => c with { Z = m.ToZ }).DefaultIfEmpty(c).First())
            .ToArray();

        var second = OutlineCornerPlanner.Plan(applied, target, first.NewRecords);

        Assert.Empty(second.Raises);
        Assert.Empty(second.PutBacks);
        Assert.Equal(0, second.NewlyByHand);
        Assert.Equal(first.NewRecords, second.NewRecords);
    }

    // ---------------------------------------------------------------- IsLeftToCornerPass (ruling R1)

    [Fact]
    public void AnEdgePointWithinOneMillimetreOfAnOutlineVertexIsLeftToTheCornerPass()
    {
        var corners = new[] { new XyzPoint(10, 0, 3), new XyzPoint(0, 0, 3) };

        Assert.True(OutlineCornerPlanner.IsLeftToCornerPass(new XyzPoint(0, 0, 7), corners));
        Assert.True(OutlineCornerPlanner.IsLeftToCornerPass(new XyzPoint(0.6 * Mm, 0.79 * Mm, 7), corners));
        Assert.True(OutlineCornerPlanner.IsLeftToCornerPass(new XyzPoint(10 - 0.999 * Mm, 0, 7), corners));
    }

    [Fact]
    public void AnEdgePointMoreThanOneMillimetreFromEveryOutlineVertexIsDrawn()
    {
        var corners = new[] { new XyzPoint(10, 0, 3), new XyzPoint(0, 0, 3) };

        Assert.False(OutlineCornerPlanner.IsLeftToCornerPass(new XyzPoint(1.001 * Mm, 0, 3), corners));
        Assert.False(OutlineCornerPlanner.IsLeftToCornerPass(new XyzPoint(0.75 * Mm, 0.75 * Mm, 3), corners));
        Assert.False(OutlineCornerPlanner.IsLeftToCornerPass(new XyzPoint(5, 0, 3), corners));
        Assert.False(OutlineCornerPlanner.IsLeftToCornerPass(new XyzPoint(0, 0, 3), None));
    }

    // ---------------------------------------------------------------- NeedsShapeEditing (ruling R2)

    [Fact]
    public void APlainOutlineNoLineInfluencesNeedsNoShapeEditing()
    {
        Assert.False(OutlineCornerPlanner.NeedsShapeEditing(SquareWithHoleCorners, 3, Always(null)));
    }

    [Fact]
    public void APlainOutlineWhoseCornersTheLinesLeaveWithinATenthOfAMillimetreNeedsNoShapeEditing()
    {
        Assert.False(OutlineCornerPlanner.NeedsShapeEditing(SquareWithHoleCorners, 3, Always(3 + 0.099 * Mm)));
        Assert.False(OutlineCornerPlanner.NeedsShapeEditing(SquareWithHoleCorners, 3, Always(3 - 0.099 * Mm)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonFiniteTargetNeedsNoShapeEditing(double bad)
    {
        Assert.False(OutlineCornerPlanner.NeedsShapeEditing(SquareWithHoleCorners, 3, Always(bad)));
    }

    [Fact]
    public void OneOutlinePointTheLinesMoveNeedsShapeEditing()
    {
        // The hole's corner (6, 6) only, by a little more than a tenth of a millimetre.
        Func<XyzPoint, double?> target = v => v.X == 6 && v.Y == 6 ? 3 + 0.101 * Mm : null;

        Assert.True(OutlineCornerPlanner.NeedsShapeEditing(SquareWithHoleCorners, 3, target));
    }

    [Fact]
    public void EveryOutlineCornerIsAskedAtTheFlatHeight()
    {
        var asked = new List<XyzPoint>();
        OutlineCornerPlanner.NeedsShapeEditing(SquareWithHoleCorners, 3, v => { asked.Add(v); return null; });

        Assert.Equal(
            SquareWithHole.SelectMany(loop => loop).Select(p => new XyzPoint(p.X, p.Y, 3)).OrderBy(p => p.X).ThenBy(p => p.Y),
            asked.OrderBy(p => p.X).ThenBy(p => p.Y));
    }

    // ---------------------------------------------------------------- Reset Shape (fix round 1, item 3)

    [Fact]
    public void ARecordedCornerBackAtItsOwnHeightWasResetAndIsPlannedAsANewCorner()
    {
        // Reset Shape put the corner back to its own height: not a hand edit.
        var corner = new XyzPoint(0, 0, 10);
        var record = new CornerRecord(0, 0, 10, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(12), new[] { record });

        Assert.Equal(new[] { (corner, 12.0) }, plan.Raises);
        Assert.Equal(new[] { new CornerRecord(0, 0, 10, 12, false) }, plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
    }

    [Fact]
    public void AResetCornerNoLineInfluencesLosesItsRecordAndIsNotPutBack()
    {
        var corner = new XyzPoint(0, 0, 10 + 0.09 * Mm);
        var record = new CornerRecord(0, 0, 10, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(null), new[] { record });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Empty(plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
    }

    [Fact]
    public void AResetCornerStartsAFreshRecordWithItsCurrentHeight()
    {
        double now = 10 - 0.09 * Mm;
        var corner = new XyzPoint(0, 0, now);
        var record = new CornerRecord(0, 0, 10, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(14), new[] { record });

        Assert.Equal(new[] { new CornerRecord(0, 0, now, 14, false) }, plan.NewRecords);
    }

    [Fact]
    public void ACornerJustOverATenthOfAMillimetreFromItsOwnHeightIsStillMovedByHand()
    {
        var corner = new XyzPoint(0, 0, 10 + 0.11 * Mm);
        var record = new CornerRecord(0, 0, 10, 12, false);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(12), new[] { record });

        Assert.Empty(plan.Raises);
        Assert.Equal(new[] { record with { ByHand = true } }, plan.NewRecords);
        Assert.Equal(1, plan.NewlyByHand);
    }

    [Fact]
    public void ACornerFlaggedByHandBackAtItsOwnHeightStaysFlagged()
    {
        var corner = new XyzPoint(0, 0, 10);
        var record = new CornerRecord(0, 0, 10, 12, true);

        var plan = OutlineCornerPlanner.Plan(new[] { corner }, Always(12), new[] { record });

        Assert.Empty(plan.Raises);
        Assert.Equal(new[] { record }, plan.NewRecords);
    }

    // ---------------------------------------------------------------- HandMovedRecords (fix round 1, item 1)

    [Fact]
    public void ALedgeredPointFoundAtItsSpotButAnotherHeightBecomesAHandRecord()
    {
        var unmatched = new[] { new XyzPoint(5, 0, 12) };
        var vertices = new[] { new XyzPoint(0, 0, 10), new XyzPoint(5, 0, 13.5), new XyzPoint(10, 0, 10) };

        var records = OutlineCornerPlanner.HandMovedRecords(unmatched, vertices);

        Assert.Equal(new[] { new CornerRecord(5, 0, 13.5, 13.5, true) }, records);
    }

    [Fact]
    public void ALedgeredPointWithNoVertexAtItsSpotGivesNoRecord()
    {
        var records = OutlineCornerPlanner.HandMovedRecords(
            new[] { new XyzPoint(5, 0, 12) },
            new[] { new XyzPoint(5 + 0.11 * Mm, 0, 13), new XyzPoint(5, 0.11 * Mm, 13) });

        Assert.Empty(records);
    }

    [Fact]
    public void TheSpotIsATenthOfAMillimetreOnEachAxis()
    {
        var vertex = new XyzPoint(5 + 0.09 * Mm, -0.09 * Mm, 13);

        var records = OutlineCornerPlanner.HandMovedRecords(new[] { new XyzPoint(5, 0, 12) }, new[] { vertex });

        Assert.Equal(new[] { new CornerRecord(vertex.X, vertex.Y, 13, 13, true) }, records);
    }

    [Fact]
    public void TwoLedgeredPointsAtOneVertexGiveOneRecord()
    {
        var records = OutlineCornerPlanner.HandMovedRecords(
            new[] { new XyzPoint(5, 0, 12), new XyzPoint(5, 0, 11) }, new[] { new XyzPoint(5, 0, 13) });

        Assert.Single(records);
    }

    // ---------------------------------------------------------------- ExceptAt (fix round 2, item 1)

    [Fact]
    public void AVertexRevitWouldNotDeleteIsLeftOutOfThisRunsCandidatesOnly()
    {
        // A ledgered point whose DeletePoint failed: nobody moved it, so no hand record — it is
        // only kept out of this run's pass (with its spot's Z possibly changed since).
        var candidates = new[] { new XyzPoint(0, 0, 10), new XyzPoint(5, 0, 12.5), new XyzPoint(10, 0, 10) };

        var kept = OutlineCornerPlanner.ExceptAt(candidates, new[] { new XyzPoint(5, 0, 12) });

        Assert.Equal(new[] { candidates[0], candidates[2] }, kept);
    }

    [Fact]
    public void ExceptAtMatchesATenthOfAMillimetreOnEachAxisInPlan()
    {
        var near = new XyzPoint(5 + 0.09 * Mm, -0.09 * Mm, 1);
        var off = new XyzPoint(5 + 0.11 * Mm, 0, 1);

        var kept = OutlineCornerPlanner.ExceptAt(new[] { near, off }, new[] { new XyzPoint(5, 0, 12) });

        Assert.Equal(new[] { off }, kept);
    }

    [Fact]
    public void ExceptAtNothingKeepsEveryCandidate()
    {
        var candidates = new[] { new XyzPoint(0, 0, 10), new XyzPoint(5, 0, 12) };

        Assert.Equal(candidates, OutlineCornerPlanner.ExceptAt(candidates, None));
    }

    [Fact]
    public void AHandMovedLinePointOnTheOutlineIsLeftAloneByThePassAndStaysFlagged()
    {
        // The line's ledger entry is gone, so the vertex is an outline candidate; its hand record
        // keeps the pass off it, now and on every later run, and it is not reported a second time.
        var vertex = new XyzPoint(5, 0, 13.5);
        var hand = OutlineCornerPlanner.HandMovedRecords(new[] { new XyzPoint(5, 0, 12) }, new[] { vertex });
        bool asked = false;

        var plan = OutlineCornerPlanner.Plan(new[] { vertex }, _ => { asked = true; return 20; }, hand);
        var next = OutlineCornerPlanner.Plan(new[] { vertex }, Always(20), plan.NewRecords);

        Assert.Empty(plan.Raises);
        Assert.Equal(hand, plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
        Assert.False(asked);
        Assert.Empty(next.Raises);
        Assert.Equal(hand, next.NewRecords);
    }

    // ---------------------------------------------------------------- Settle (fix round 1, items 2, 4, 6)

    private static Dictionary<(double X, double Y), double> Reached(params (double X, double Y, double Z)[] entries) =>
        entries.ToDictionary(e => (e.X, e.Y), e => e.Z);

    [Fact]
    public void CornersSetCountsEveryCornerHoldingALineHeightRaisedNowOrEarlier()
    {
        var candidates = new[]
        {
            new XyzPoint(0, 0, 10),   // raised now
            new XyzPoint(10, 0, 12),  // recorded earlier, already at its target
            new XyzPoint(10, 10, 15), // moved by hand
        };
        var records = new[] { new CornerRecord(10, 0, 9, 12, false), new CornerRecord(10, 10, 9, 12, false) };
        var plan = OutlineCornerPlanner.Plan(candidates, Always(12), records);

        var outcome = OutlineCornerPlanner.Settle(plan, Reached((0, 0, 12)), Reached());

        Assert.Equal(2, outcome.Set);
        Assert.Equal(0, outcome.PutBack);
        Assert.Equal(0, outcome.Lost);
        Assert.Equal(1, outcome.Changed);
    }

    [Fact]
    public void ARaiseThatEndsOffItsHeightIsLostNotSetAndRecordsWhereItIs()
    {
        var plan = OutlineCornerPlanner.Plan(new[] { new XyzPoint(0, 0, 10) }, Always(12), NoRecords);

        var outcome = OutlineCornerPlanner.Settle(plan, Reached((0, 0, 11)), Reached());

        Assert.Equal(0, outcome.Set);
        Assert.Equal(1, outcome.Lost);
        Assert.Equal(1, outcome.Changed);
        Assert.Equal(new[] { new CornerRecord(0, 0, 10, 11, false) }, outcome.Records);
    }

    [Fact]
    public void ARaiseThatDidNotMoveTheCornerChangedNothing()
    {
        var plan = OutlineCornerPlanner.Plan(new[] { new XyzPoint(0, 0, 10) }, Always(12), NoRecords);

        var outcome = OutlineCornerPlanner.Settle(plan, Reached((0, 0, 10 + 0.09 * Mm)), Reached());

        Assert.Equal(1, outcome.Lost);
        Assert.Equal(0, outcome.Changed);
    }

    [Fact]
    public void APutBackThatArrivesIsCountedAndLeavesNoRecord()
    {
        var plan = OutlineCornerPlanner.Plan(
            new[] { new XyzPoint(0, 0, 12) }, Always(null), new[] { new CornerRecord(0, 0, 10, 12, false) });

        var outcome = OutlineCornerPlanner.Settle(plan, Reached(), Reached((0, 0, 10 + 0.05 * Mm)));

        Assert.Equal(1, outcome.PutBack);
        Assert.Equal(0, outcome.Set);
        Assert.Equal(0, outcome.Lost);
        Assert.Equal(1, outcome.Changed);
        Assert.Empty(outcome.Records);
    }

    [Fact]
    public void APutBackThatFailsKeepsARecordWithTheOwnHeightWhereTheCornerNowStands()
    {
        var plan = OutlineCornerPlanner.Plan(
            new[] { new XyzPoint(0, 0, 12) }, Always(null), new[] { new CornerRecord(0, 0, 10, 12, false) });

        var outcome = OutlineCornerPlanner.Settle(plan, Reached(), Reached((0, 0, 12)));

        Assert.Equal(0, outcome.PutBack);
        Assert.Equal(1, outcome.Lost);
        Assert.Equal(0, outcome.Changed);
        Assert.Equal(new[] { new CornerRecord(0, 0, 10, 12, false) }, outcome.Records);

        // The next run, with no line still, tries again rather than calling it moved by hand.
        var next = OutlineCornerPlanner.Plan(new[] { new XyzPoint(0, 0, 12) }, Always(null), outcome.Records);
        Assert.Equal(new[] { (new XyzPoint(0, 0, 12), 10.0) }, next.PutBacks);
        Assert.Equal(0, next.NewlyByHand);
    }

    [Fact]
    public void SettledRecordsAreSortedByXThenY()
    {
        var candidates = new[] { new XyzPoint(10, 0, 12), new XyzPoint(0, 5, 10), new XyzPoint(0, 0, 12) };
        var records = new[] { new CornerRecord(10, 0, 9, 12, false), new CornerRecord(0, 0, 9, 12, false) };
        Func<XyzPoint, double?> target = p => p.Y == 5 ? 11 : null;
        var plan = OutlineCornerPlanner.Plan(candidates, target, records);

        // Both put-backs fail; the raise arrives.
        var outcome = OutlineCornerPlanner.Settle(plan, Reached((0, 5, 11)), Reached((0, 0, 12), (10, 0, 12)));

        Assert.Equal(new[] { (0.0, 0.0), (0.0, 5.0), (10.0, 0.0) }, outcome.Records.Select(r => (r.X, r.Y)));
    }

    [Fact]
    public void SettleThrowsNamingAPutBackItHasNoHeightFor()
    {
        var plan = OutlineCornerPlanner.Plan(
            new[] { new XyzPoint(1.5, 2.25, 12) }, Always(null), new[] { new CornerRecord(1.5, 2.25, 10, 12, false) });

        var error = Assert.Throws<ArgumentException>(() => OutlineCornerPlanner.Settle(plan, Reached(), Reached()));

        Assert.Contains("X = 1.5, Y = 2.25", error.Message);
    }

    [Fact]
    public void AReRunWithNothingChangedStillCountsTheCornersSet()
    {
        // Accept after a quiet Update names the corners holding a line height (item 2).
        var corner = new XyzPoint(0, 0, 10);
        var first = OutlineCornerPlanner.Plan(new[] { corner }, Always(12), NoRecords);
        var settled = OutlineCornerPlanner.Settle(first, Reached((0, 0, 12)), Reached());

        var second = OutlineCornerPlanner.Plan(new[] { corner with { Z = 12 } }, Always(12), settled.Records);
        var again = OutlineCornerPlanner.Settle(second, Reached(), Reached());

        Assert.Equal(1, again.Set);
        Assert.Equal(0, again.Changed);
        Assert.Equal(settled.Records, again.Records);
    }

    // ---------------------------------------------------------------- Restore (Cancel, Task 5)

    [Fact]
    public void ACornerTwoUpdatesMovedGoesBackToItsHeightAtSessionStartNotTheFirstUpdates()
    {
        // Review Focus 4. An earlier session set the corner to 10 (own height 9); this session's
        // first Update took it to 11, its second to 12.
        var atStart = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var now = new[] { new CornerRecord(0, 0, 9, 12, false) };
        var corner = new XyzPoint(0, 0, 12);

        var plan = OutlineCornerPlanner.Restore(atStart, now, new[] { corner });

        Assert.Equal(new[] { (corner, 10.0) }, plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(atStart, plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
    }

    [Fact]
    public void ACornerOnlyTheSessionMovedGoesBackToItsOwnHeightAndKeepsNoRecord()
    {
        // No record at the start: two Updates took it from 10 to 11, then 12.
        var now = new[] { new CornerRecord(0, 0, 10, 12, false) };
        var corner = new XyzPoint(0, 0, 12);

        var plan = OutlineCornerPlanner.Restore(NoRecords, now, new[] { corner });

        Assert.Empty(plan.Raises);
        Assert.Equal(new[] { (corner, 10.0) }, plan.PutBacks);
        Assert.Empty(plan.NewRecords);
    }

    [Fact]
    public void ACornerTheSessionPutBackIsRaisedAgainToItsHeightAtSessionStart()
    {
        // Its line was deleted during the session and Update put it back to its own height,
        // dropping the record: the record changed, so Cancel restores it.
        var atStart = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var corner = new XyzPoint(0, 0, 9);

        var plan = OutlineCornerPlanner.Restore(atStart, NoRecords, new[] { corner });

        Assert.Equal(new[] { (corner, 10.0) }, plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(atStart, plan.NewRecords);
    }

    [Fact]
    public void ACornerFlaggedByHandDuringTheSessionIsLeftAloneAndKeepsItsFlag()
    {
        var hand = new CornerRecord(0, 0, 9, 11, true);
        var handNoStart = new CornerRecord(10, 0, 9, 15, true);
        var atStart = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var now = new[] { hand, handNoStart };

        var plan = OutlineCornerPlanner.Restore(atStart, now,
            new[] { new XyzPoint(0, 0, 14), new XyzPoint(10, 0, 15) });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(new[] { hand, handNoStart }, plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
    }

    [Fact]
    public void ACornerFlaggedByHandAtTheStartIsLeftAlone()
    {
        var hand = new CornerRecord(0, 0, 9, 11, true);

        var same = OutlineCornerPlanner.Restore(new[] { hand }, new[] { hand }, new[] { new XyzPoint(0, 0, 3) });
        var dropped = OutlineCornerPlanner.Restore(new[] { hand }, NoRecords, new[] { new XyzPoint(0, 0, 3) });
        // Its record replaced during the session by one not flagged (the corner was gone, then
        // came back): still the start's hand corner.
        var replaced = OutlineCornerPlanner.Restore(new[] { hand },
            new[] { new CornerRecord(0, 0, 3, 12, false) }, new[] { new XyzPoint(0, 0, 12) });

        foreach (var plan in new[] { same, dropped, replaced })
        {
            Assert.Empty(plan.Raises);
            Assert.Empty(plan.PutBacks);
            Assert.Equal(new[] { hand }, plan.NewRecords);
        }
    }

    [Fact]
    public void UnchangedRecordsGiveAnEmptyPlanEvenForACornerOffItsSetHeight()
    {
        // The corner at (10, 0) stands off its set height (moved by hand after the last Update,
        // not yet flagged): its record did not change, so Cancel does not touch it.
        var records = new[] { new CornerRecord(0, 0, 9, 10, false), new CornerRecord(10, 0, 9, 10, false) };

        var plan = OutlineCornerPlanner.Restore(records, records,
            new[] { new XyzPoint(0, 0, 10), new XyzPoint(10, 0, 13) });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(records, plan.NewRecords);
        Assert.Equal(0, plan.NewlyByHand);
    }

    [Fact]
    public void NoRecordsGiveAnEmptyPlan()
    {
        var plan = OutlineCornerPlanner.Restore(NoRecords, NoRecords, new[] { new XyzPoint(0, 0, 10) });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Empty(plan.NewRecords);
    }

    [Fact]
    public void ASetHeightWithinATenthOfAMillimetreOfTheStartsIsUnchanged()
    {
        var atStart = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var corner = new XyzPoint(0, 0, 13);

        var plan = OutlineCornerPlanner.Restore(atStart,
            new[] { new CornerRecord(0, 0, 9, 10 + 0.099 * Mm, false) }, new[] { corner });
        var moved = OutlineCornerPlanner.Restore(atStart,
            new[] { new CornerRecord(0, 0, 9, 10 + 0.101 * Mm, false) }, new[] { corner });

        Assert.Empty(plan.Raises);
        Assert.Equal(atStart, plan.NewRecords);
        Assert.Equal(new[] { (corner, 10.0) }, moved.Raises);
    }

    [Fact]
    public void AChangedCornerAlreadyWithinATenthOfAMillimetreOfItsStartHeightIsNotMoved()
    {
        var atStart = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var now = new[] { new CornerRecord(0, 0, 9, 12, false) };

        var raise = OutlineCornerPlanner.Restore(atStart, now, new[] { new XyzPoint(0, 0, 10 + 0.099 * Mm) });
        var putBack = OutlineCornerPlanner.Restore(NoRecords, now, new[] { new XyzPoint(0, 0, 9 - 0.099 * Mm) });
        var raiseJustOff = OutlineCornerPlanner.Restore(atStart, now, new[] { new XyzPoint(0, 0, 10 + 0.101 * Mm) });
        var putBackJustOff = OutlineCornerPlanner.Restore(NoRecords, now, new[] { new XyzPoint(0, 0, 9 - 0.101 * Mm) });

        Assert.Empty(raise.Raises);
        Assert.Equal(atStart, raise.NewRecords);
        Assert.Empty(putBack.PutBacks);
        Assert.Empty(putBack.NewRecords);
        Assert.Single(raiseJustOff.Raises);
        Assert.Single(putBackJustOff.PutBacks);
    }

    [Fact]
    public void AStartRecordWhoseCornerIsGoneIsKeptAndANewRecordWhoseCornerIsGoneIsDropped()
    {
        // Edit Boundary during the session: nothing to move. The start's record comes back (the
        // next Update drops it, as it drops every record with no corner).
        var atStart = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var now = new[] { new CornerRecord(10, 0, 9, 12, false) };

        var plan = OutlineCornerPlanner.Restore(atStart, now, new[] { new XyzPoint(5, 5, 3) });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(atStart, plan.NewRecords);
    }

    [Fact]
    public void RecordsPairWithCornersAndWithEachOtherWithinATenthOfAMillimetreInPlan()
    {
        var atStart = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var now = new[] { new CornerRecord(0.07 * Mm, 0, 9, 12, false) };

        var plan = OutlineCornerPlanner.Restore(atStart, now, new[] { new XyzPoint(0.05 * Mm, 0.05 * Mm, 12) });

        // The raise names the start record's spot, which RaiseVertexTo finds within 0.1 mm, so
        // WithReached keys the restored record by it.
        Assert.Equal(new[] { (new XyzPoint(0, 0, 12), 10.0) }, plan.Raises);
        Assert.Equal(atStart, plan.NewRecords);

        // A corner 0.2 mm away is another corner: nothing to move.
        var far = OutlineCornerPlanner.Restore(atStart, now, new[] { new XyzPoint(0.2 * Mm, 0, 12) });
        Assert.Empty(far.Raises);
        Assert.Empty(far.PutBacks);

        // Records 0.2 mm apart are two corners: the start's is dropped by the session, the new
        // one is the session's own.
        var apart = OutlineCornerPlanner.Restore(atStart,
            new[] { new CornerRecord(0.2 * Mm, 0, 8, 12, false) },
            new[] { new XyzPoint(0, 0, 9), new XyzPoint(0.2 * Mm, 0, 12) });
        Assert.Equal(new[] { (new XyzPoint(0, 0, 9), 10.0) }, apart.Raises);
        Assert.Equal(new[] { (new XyzPoint(0.2 * Mm, 0, 12), 8.0) }, apart.PutBacks);
        Assert.Equal(atStart, apart.NewRecords);
    }

    [Fact]
    public void RestoreDoesNotDependOnTheOrderOfAnyList()
    {
        var atStart = new[]
        {
            new CornerRecord(0, 0, 9, 10, false),
            new CornerRecord(10, 0, 9, 10, false),
            new CornerRecord(10, 10, 9, 10, false),
        };
        var now = new[]
        {
            new CornerRecord(0, 0, 9, 12, false),  // moved: back to 10
            new CornerRecord(10, 10, 9, 15, true), // flagged during the session
            new CornerRecord(0, 10, 8, 11, false), // the session's own: back to 8
        };
        var vertices = new[]
        {
            new XyzPoint(0, 0, 12), new XyzPoint(10, 0, 9), new XyzPoint(10, 10, 15), new XyzPoint(0, 10, 11),
        };

        var expected = OutlineCornerPlanner.Restore(atStart, now, vertices);
        // (0, 0), and (10, 0) whose record the session dropped.
        Assert.Equal(new[] { (vertices[0], 10.0), (vertices[1], 10.0) }, expected.Raises);
        Assert.Equal(new[] { (vertices[3], 8.0) }, expected.PutBacks);
        Assert.Equal(new[] { atStart[0], atStart[1], now[1] }, expected.NewRecords);

        foreach (var s in Orders(atStart))
        foreach (var n in Orders(now))
        foreach (var v in Orders(vertices))
            AssertSamePlan(expected, OutlineCornerPlanner.Restore(s, n, v));
    }

    [Fact]
    public void ASettledRestoreWritesTheStartRecordTextBack()
    {
        var atStart = new[] { new CornerRecord(0, 0, 9, 10, false), new CornerRecord(10, 0, 9, 10.5, false) };
        var now = new[] { new CornerRecord(0, 0, 9, 12, false), new CornerRecord(10, 10, 8, 11, false) };
        var vertices = new[] { new XyzPoint(0, 0, 12), new XyzPoint(10, 0, 9), new XyzPoint(10, 10, 11) };

        var plan = OutlineCornerPlanner.Restore(atStart, now, vertices);
        var outcome = OutlineCornerPlanner.Settle(plan,
            Reached((0, 0, 10), (10, 0, 10.5)), Reached((10, 10, 8)));

        Assert.Equal(OutlineCornerRecordCodec.Encode(atStart), OutlineCornerRecordCodec.Encode(outcome.Records));
        Assert.Equal(0, outcome.Lost);
        Assert.Equal(3, outcome.Changed);
    }

    [Fact]
    public void ARestoreThatFailsRecordsWhereTheCornerStands()
    {
        // So the next Update does not read it as moved by hand.
        var atStart = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var now = new[] { new CornerRecord(0, 0, 9, 12, false), new CornerRecord(10, 0, 8, 11, false) };
        var vertices = new[] { new XyzPoint(0, 0, 12), new XyzPoint(10, 0, 11) };

        var plan = OutlineCornerPlanner.Restore(atStart, now, vertices);
        var outcome = OutlineCornerPlanner.Settle(plan, Reached((0, 0, 12)), Reached((10, 0, 11)));

        Assert.Equal(2, outcome.Lost);
        Assert.Equal(0, outcome.Changed);
        Assert.Equal(new[] { new CornerRecord(0, 0, 9, 12, false), new CornerRecord(10, 0, 8, 11, false) },
            outcome.Records);

        var next = OutlineCornerPlanner.Plan(vertices, Always(null), outcome.Records);
        Assert.Equal(0, next.NewlyByHand);
    }

    [Fact]
    public void WhenTheStartAndTheCurrentRecordAreBothByHandTheStartsIsKept()
    {
        var atStart = new CornerRecord(0, 0, 9, 11, true);
        var now = new CornerRecord(0, 0, 9, 14, true);

        var plan = OutlineCornerPlanner.Restore(new[] { atStart }, new[] { now }, new[] { new XyzPoint(0, 0, 14) });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(new[] { atStart }, plan.NewRecords);
    }

    // ---------------------------------------------------------------- EffectiveStart (Task 5, fix round 1)

    [Fact]
    public void AFreshRecordIsUnchangedByTheEffectiveStart()
    {
        var stored = new[]
        {
            new CornerRecord(0, 0, 9, 10, false),
            new CornerRecord(10, 0, 9, 11, true),              // by hand, off its set height
            new CornerRecord(10, 10, 8, 8.5, false),
        };
        var vertices = new[]
        {
            new XyzPoint(0, 0, 10 + 0.099 * Mm), new XyzPoint(10, 0, 13), new XyzPoint(10, 10, 8.5),
            new XyzPoint(5, 5, 3), // no record
        };

        Assert.Equal(stored, OutlineCornerPlanner.EffectiveStart(stored, true, vertices));
    }

    [Fact]
    public void WithShapeEditingOffTheEffectiveStartHasNoRecord()
    {
        var stored = new[] { new CornerRecord(0, 0, 9, 10, false) };

        Assert.Empty(OutlineCornerPlanner.EffectiveStart(stored, false, new[] { new XyzPoint(0, 0, 10) }));
    }

    [Fact]
    public void ACornerResetBeforeTheSessionLosesItsRecordAtTheStart()
    {
        var stored = new[] { new CornerRecord(0, 0, 9, 10, false) };

        var atOwn = OutlineCornerPlanner.EffectiveStart(stored, true, new[] { new XyzPoint(0, 0, 9 + 0.099 * Mm) });
        var justOff = OutlineCornerPlanner.EffectiveStart(stored, true, new[] { new XyzPoint(0, 0, 9 + 0.101 * Mm) });

        Assert.Empty(atOwn);
        Assert.Equal(new[] { new CornerRecord(0, 0, 9, 9 + 0.101 * Mm, true) }, justOff);
    }

    [Fact]
    public void ACornerMovedByHandBeforeTheSessionIsFlaggedAtTheStartWhereItStands()
    {
        var stored = new[] { new CornerRecord(0, 0, 9, 10, false) };

        var start = OutlineCornerPlanner.EffectiveStart(stored, true, new[] { new XyzPoint(0, 0, 13) });

        Assert.Equal(new[] { new CornerRecord(0, 0, 9, 13, true) }, start);
    }

    [Fact]
    public void ARecordWithNoCornerIsLeftOutOfTheEffectiveStart()
    {
        var stored = new[] { new CornerRecord(0, 0, 9, 10, false), new CornerRecord(10, 0, 9, 10, true) };

        var start = OutlineCornerPlanner.EffectiveStart(stored, true, new[] { new XyzPoint(0.2 * Mm, 0, 10) });

        Assert.Empty(start);
    }

    [Fact]
    public void TheEffectiveStartPairsRecordsAndCornersWithinATenthOfAMillimetreInEveryOrder()
    {
        var stored = new[]
        {
            new CornerRecord(0, 0, 9, 10, false),
            new CornerRecord(0.15 * Mm, 0, 9, 12, false),
        };
        var vertices = new[] { new XyzPoint(0.05 * Mm, 0, 10), new XyzPoint(0.2 * Mm, 0, 12) };

        var expected = OutlineCornerPlanner.EffectiveStart(stored, true, vertices);
        Assert.Equal(stored, expected);

        foreach (var s in Orders(stored))
        foreach (var v in Orders(vertices))
            Assert.Equal(expected, OutlineCornerPlanner.EffectiveStart(s, true, v));
    }

    [Fact]
    public void CancelAfterAResetToposolidsUpdateOnlyUndoesTheUpdate()
    {
        // Reset Shape outside a session left the corner at its own height 9 with the old record
        // (set 10). The session's Update planned it as new and raised it to 11.
        var stored = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var start = OutlineCornerPlanner.EffectiveStart(stored, true, new[] { new XyzPoint(0, 0, 9) });
        var update = OutlineCornerPlanner.Plan(new[] { new XyzPoint(0, 0, 9) }, Always(11), stored);
        var corner = new XyzPoint(0, 0, 11);

        var plan = OutlineCornerPlanner.Restore(start, update.NewRecords, new[] { corner });

        Assert.Empty(plan.Raises);
        Assert.Equal(new[] { (corner, 9.0) }, plan.PutBacks);
        Assert.Empty(plan.NewRecords);
    }

    [Fact]
    public void CancelAfterAPlainFloorsUpdatePutsItsCornersBackToTheFloorsTop()
    {
        // Reset Shape made the floor plain with an earlier session's record left (set 10); the
        // session's Update switched shape editing on and raised the corner from 9 to 11.
        var stored = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var start = OutlineCornerPlanner.EffectiveStart(stored, false, new[] { new XyzPoint(0, 0, 9) });
        var now = new[] { new CornerRecord(0, 0, 9, 11, false) };
        var corner = new XyzPoint(0, 0, 11);

        var plan = OutlineCornerPlanner.Restore(start, now, new[] { corner });

        Assert.Empty(plan.Raises);
        Assert.Equal(new[] { (corner, 9.0) }, plan.PutBacks);
        Assert.Empty(plan.NewRecords);
    }

    [Fact]
    public void CancelLeavesACornerMovedByHandBeforeTheSessionWhereItIs()
    {
        var stored = new[] { new CornerRecord(0, 0, 9, 10, false) };
        var corner = new XyzPoint(0, 0, 13);
        var start = OutlineCornerPlanner.EffectiveStart(stored, true, new[] { corner });
        var update = OutlineCornerPlanner.Plan(new[] { corner }, Always(11), stored); // flags it

        var plan = OutlineCornerPlanner.Restore(start, update.NewRecords, new[] { corner });

        Assert.Empty(plan.Raises);
        Assert.Empty(plan.PutBacks);
        Assert.Equal(start, plan.NewRecords);
    }
}
