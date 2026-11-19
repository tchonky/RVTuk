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
}
