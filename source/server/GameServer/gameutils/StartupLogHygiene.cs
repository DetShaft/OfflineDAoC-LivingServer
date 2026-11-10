using System.Collections.Concurrent;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Demotes expected world-data startup noise to one WARN per key per session.
    /// Real load failures stay at ERR. See docs/STARTUP-LOG-HYGIENE.md.
    /// </summary>
    public static class StartupLogHygiene
    {
        private static readonly ConcurrentDictionary<string, byte> Reported = new();
        private static int _demoted;

        public static int DemotedCount => Volatile.Read(ref _demoted);

        public static bool WarnOnce(Logger logger, string category, string key, string message)
        {
            string fullKey = category + ":" + key;
            if (!Reported.TryAdd(fullKey, 0))
                return false;

            Interlocked.Increment(ref _demoted);
            if (logger.IsWarnEnabled)
                logger.Warn(message);
            return true;
        }

        public static void LogSessionSummary(Logger logger)
        {
            int count = Volatile.Read(ref _demoted);
            if (count <= 0 || !logger.IsInfoEnabled)
                return;

            logger.Info($"STARTUP_LOG_HYGIENE demoted={count} unique expected data references (once per key at WRN; see docs/STARTUP-LOG-HYGIENE.md)");
        }
    }
}
