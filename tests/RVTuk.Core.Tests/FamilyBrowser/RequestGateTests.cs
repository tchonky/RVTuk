using System;
using System.Threading;
using System.Threading.Tasks;
using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

public class RequestGateTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    [Fact]
    public void Wait_ReturnsAfterComplete_WhenStartedInTime()
    {
        var gate = new RequestGate();
        gate.Begin();
        var worker = Task.Run(() =>
        {
            Assert.True(gate.TryStart());
            Thread.Sleep(100);
            gate.Complete();
        });

        gate.Wait(TimeSpan.FromSeconds(1));

        Assert.True(worker.IsCompleted);
    }

    [Fact]
    public void Wait_Throws_WhenNotStartedInTime()
    {
        var gate = new RequestGate();
        gate.Begin();

        var ex = Assert.Throws<RevitBusyException>(() => gate.Wait(Short));

        Assert.Equal("Revit is busy (a dialog is open or a command is running). Finish it and try again.", ex.Message);
    }

    [Fact]
    public void TryStart_AfterWithdraw_ReturnsFalse()
    {
        var gate = new RequestGate();
        gate.Begin();
        Assert.Throws<RevitBusyException>(() => gate.Wait(Short));

        Assert.False(gate.TryStart());
    }

    [Fact]
    public void Withdrawn_ThenBegin_NextTryStartServesNewRequest()
    {
        var gate = new RequestGate();
        gate.Begin();
        Assert.Throws<RevitBusyException>(() => gate.Wait(Short));

        gate.Begin();
        Assert.True(gate.TryStart());
        gate.Complete();
        gate.Wait(Short);
    }

    [Fact]
    public void Wait_DoesNotTimeOut_OnceStarted()
    {
        var gate = new RequestGate();
        gate.Begin();
        Assert.True(gate.TryStart());
        var worker = Task.Run(() => { Thread.Sleep(300); gate.Complete(); });

        gate.Wait(Short);

        Assert.True(worker.IsCompleted);
    }

    [Fact]
    public void TryStart_WithoutBegin_ReturnsFalse()
    {
        Assert.False(new RequestGate().TryStart());
    }
}
