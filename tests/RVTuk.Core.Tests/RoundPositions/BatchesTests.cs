using System;
using System.Linq;
using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class BatchesTests
{
    private static int[] Range(int n) => Enumerable.Range(1, n).ToArray();

    [Fact]
    public void AnExactMultipleSplitsEvenly()
    {
        var batches = Batches.Split(Range(6), 3);

        Assert.Equal(2, batches.Count);
        Assert.Equal(new[] { 1, 2, 3 }, batches[0]);
        Assert.Equal(new[] { 4, 5, 6 }, batches[1]);
    }

    [Fact]
    public void ARemainderBecomesAShorterFinalBatch()
    {
        var batches = Batches.Split(Range(7), 3);

        Assert.Equal(3, batches.Count);
        Assert.Equal(new[] { 7 }, batches[2]);
    }

    [Fact]
    public void FewerItemsThanOneBatchIsOneBatch()
    {
        var batches = Batches.Split(Range(2), 250);

        Assert.Single(batches);
        Assert.Equal(2, batches[0].Count);
    }

    [Fact]
    public void NothingToSplitYieldsNoBatchesRatherThanOneEmptyOne()
    {
        // A caller looping over batches must not open a sub-transaction for nothing.
        Assert.Empty(Batches.Split(Array.Empty<int>(), 250));
    }

    [Fact]
    public void EveryItemAppearsExactlyOnceAndInOrder()
    {
        var batches = Batches.Split(Range(1000), 250);

        Assert.Equal(Range(1000), batches.SelectMany(b => b).ToArray());
    }

    [Fact]
    public void ANonPositiveSizeIsRejectedRatherThanLoopingForever()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Batches.Split(Range(3), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Batches.Split(Range(3), -1));
    }
}
