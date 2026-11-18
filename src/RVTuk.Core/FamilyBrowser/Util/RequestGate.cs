using System;
using System.Threading;

namespace RVTuk.Core.FamilyBrowser.Util
{
    /// <summary>
    /// One request from a background thread to an ExternalEvent handler. The caller gives
    /// Revit <see cref="StartTimeout"/> to start it; a request still waiting after that is
    /// withdrawn (Revit is in a dialog or a command) and Execute, if Revit runs it later,
    /// does nothing. A request that has started is waited for without a limit: a slow load
    /// (Desktop Connector downloading the file) is legitimate work.
    /// </summary>
    public sealed class RequestGate
    {
        public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

        private const int None = 0, Pending = 1, Running = 2, Done = 3, Withdrawn = 4;
        private int _state = None;
        private readonly ManualResetEventSlim _started = new(false);
        private readonly ManualResetEventSlim _done = new(false);

        /// <summary>Caller side, from the handler's Prepare: a new request is pending.</summary>
        public void Begin()
        {
            _started.Reset();
            _done.Reset();
            Volatile.Write(ref _state, Pending);
        }

        /// <summary>Handler side, first thing in Execute. False: nothing to do, return at once.</summary>
        public bool TryStart()
        {
            if (Interlocked.CompareExchange(ref _state, Running, Pending) != Pending) return false;
            _started.Set();
            return true;
        }

        /// <summary>Handler side, in Execute's finally, only after TryStart returned true.</summary>
        public void Complete()
        {
            Volatile.Write(ref _state, Done);
            _done.Set();
        }

        /// <summary>Caller side, after Raise. Throws <see cref="RevitBusyException"/> when withdrawn.</summary>
        public void Wait(TimeSpan startTimeout)
        {
            if (!_started.Wait(startTimeout)
                && Interlocked.CompareExchange(ref _state, Withdrawn, Pending) == Pending)
                throw new RevitBusyException();
            _done.Wait();
        }
    }

    public sealed class RevitBusyException : InvalidOperationException
    {
        public RevitBusyException()
            : base("Revit is busy (a dialog is open or a command is running). Finish it and try again.") { }
    }
}
