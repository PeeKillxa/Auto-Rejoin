using System;
using System.Collections.Generic;
using System.Threading;

namespace NexusRobloxManager
{
    public sealed class InstanceGate : IDisposable
    {
        private Mutex mutex;
        public static bool TryAcquire(string name, out InstanceGate gate)
        {
            gate = null;
            Mutex candidate = new Mutex(false, name);
            bool owned;
            try { owned = candidate.WaitOne(0, false); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) { candidate.Dispose(); return false; }
            gate = new InstanceGate();
            gate.mutex = candidate;
            return true;
        }
        public void Dispose()
        {
            if (mutex == null) return;
            mutex.ReleaseMutex(); mutex.Dispose(); mutex = null;
        }
    }

    public class ClientProcessIdentity
    {
        public int Pid;
        public DateTime StartedUtc;
    }

    public static class RejoinExitBarrier
    {
        public static void AssertClear(Func<List<ClientProcessIdentity>> inventory)
        {
            if (inventory().Count > 0)
                throw new InvalidOperationException("Old account client is still running; replacement launch blocked");
        }

        public static void CloseAndConfirm(Func<List<ClientProcessIdentity>> inventory,
            Action<ClientProcessIdentity> terminateAndWait, Action releaseDelay)
        {
            // Inventory must throw on an unreadable process list. Unknown is never treated as empty.
            foreach (ClientProcessIdentity identity in inventory()) terminateAndWait(identity);
            AssertClear(inventory);
            releaseDelay();
            AssertClear(inventory);
        }
    }
}
