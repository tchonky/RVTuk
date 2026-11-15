using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace RVTuk.UI.Tests
{
    /// <summary>
    /// Runs a test on its own STA thread, as WPF objects require. Every test that touches a
    /// WPF object goes through it.
    /// </summary>
    internal static class Sta
    {
        public static void Run(Action test)
        {
            ExceptionDispatchInfo? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    test();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The test didn't finish within 30 seconds.");
            failure?.Throw();
        }
    }
}
