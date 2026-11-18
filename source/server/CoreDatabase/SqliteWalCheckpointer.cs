using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using DOL.Logging;

namespace DOL.Database
{
    /// <summary>
    /// Moves the world database's WAL into the database file on a background thread every few seconds (issue #95).
    /// <para>
    /// SQLite's default auto-checkpoint runs inside the commit that grows the WAL past 1,000 pages, on the thread that
    /// committed. In this server most writes come from the game loop (bot re-gears, loot, Realm Exchange listings), so
    /// that copy of up to 4 MB into a 120 MB file, plus its flush, landed on a game tick; on Jay's PC single-row writes
    /// logged 0.3-2 s and the NPC tick that issued them stalled with them. A PASSIVE checkpoint never waits for readers or
    /// writers, so running it here costs the game loop nothing; the pooled connections turn their own auto-checkpoint off
    /// (SqliteObjectDatabase). The stop-time TRUNCATE checkpoint (SqliteSafety.Checkpoint) is unchanged.
    /// </para>
    /// </summary>
    public sealed class SqliteWalCheckpointer : IDisposable
    {
        /// <summary>How often the WAL is checkpointed.</summary>
        public const int IntervalMilliseconds = 5000;

        /// <summary>A checkpoint this slow is logged (it ran off the game loop, but says how busy the disk is).</summary>
        public const long SlowMilliseconds = 500;

        /// <summary>Page cache per pooled connection, in KiB (SQLite's default is 2 MB).</summary>
        public const int CacheKibibytes = 16384;

        private static readonly Logger log = LoggerManager.Create(typeof(SqliteWalCheckpointer));
        private static readonly ConcurrentDictionary<string, SqliteWalCheckpointer> Running = new(StringComparer.OrdinalIgnoreCase);

        private readonly string _path;
        private readonly Timer _timer;
        private int _busy;
        private int _failures;

        /// <summary>The database file this checkpointer serves.</summary>
        public string DatabasePath => _path;

        private SqliteWalCheckpointer(string path)
        {
            _path = path;
            _timer = new Timer(_ => Tick(), null, IntervalMilliseconds, IntervalMilliseconds);
        }

        /// <summary>One checkpointer per database file, started on first use.</summary>
        public static SqliteWalCheckpointer Start(string databasePath)
        {
            string path = Path.GetFullPath(databasePath);
            return Running.GetOrAdd(path, p =>
            {
                log.Info($"SQLITE_CHECKPOINTER started path=\"{p}\" interval={IntervalMilliseconds}ms");
                return new SqliteWalCheckpointer(p);
            });
        }

        /// <summary>The result of one PASSIVE checkpoint: busy (1 = something held it back), WAL pages, pages moved.</summary>
        public readonly record struct Result(long Busy, long WalPages, long Moved, long ElapsedMilliseconds);

        /// <summary>One PASSIVE checkpoint on a short-lived side connection.</summary>
        public static Result CheckpointPassive(string databasePath)
        {
            var stopwatch = Stopwatch.StartNew();
            using var connection = SqliteSafety.OpenSide(databasePath, readOnly: false, busyTimeoutMs: 1000);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(PASSIVE)";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return new Result(0, 0, 0, stopwatch.ElapsedMilliseconds);
            return new Result(Convert.ToInt64(reader.GetValue(0)), Convert.ToInt64(reader.GetValue(1)),
                Convert.ToInt64(reader.GetValue(2)), stopwatch.ElapsedMilliseconds);
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1)
                return;                                   // the last one is still running: skip, never pile up
            try
            {
                Result result = CheckpointPassive(_path);
                _failures = 0;
                if (result.ElapsedMilliseconds >= SlowMilliseconds && log.IsInfoEnabled)
                    log.Info($"SQLITE_CHECKPOINT ms={result.ElapsedMilliseconds} wal_pages={result.WalPages} moved={result.Moved} busy={result.Busy}");
            }
            catch (Exception e)
            {
                if (++_failures <= 3 && log.IsWarnEnabled)
                    log.Warn($"SQLITE_CHECKPOINT failed path=\"{_path}\" ({_failures}): {e.Message}");
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
            Running.TryRemove(_path, out _);
        }
    }
}
