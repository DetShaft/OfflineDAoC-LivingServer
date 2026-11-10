using System.Collections.Concurrent;

namespace DOL.GS
{
    /// <summary>
    /// Tracks when autonomous gamebots last posted an LFG chat line so the find-group window
    /// and /lfg list can show advertising solos without stuffing bots into GroupMgr.m_lfgPlayers.
    /// </summary>
    public static class BotLfgAds
    {
        /// <summary>How long a bot LFG chat post keeps them listed (10 minutes).</summary>
        public const int FreshMilliseconds = 10 * 60 * 1000;

        private static readonly ConcurrentDictionary<long, long> LastAdTick = new();

        public static void Note(GameBot bot)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.DatabaseID <= 0)
                return;
            LastAdTick[bot.DatabaseID] = GameLoop.GameLoopTime;
        }

        public static void Forget(GameBot bot)
        {
            if (bot?.DatabaseID > 0)
                LastAdTick.TryRemove(bot.DatabaseID, out _);
        }

        public static bool IsAdvertising(GameBot bot, long now = 0, int windowMs = FreshMilliseconds)
        {
            if (bot?.DatabaseID <= 0)
                return false;
            if (!LastAdTick.TryGetValue(bot.DatabaseID, out long at))
                return false;
            if (now <= 0)
                now = GameLoop.GameLoopTime;
            return now >= at && now - at <= windowMs;
        }

        /// <summary>Test helper: seed or clear an ad timestamp by bot id.</summary>
        public static void SetAdTickForTests(long botId, long tick)
        {
            if (botId <= 0)
                return;
            if (tick <= 0)
                LastAdTick.TryRemove(botId, out _);
            else
                LastAdTick[botId] = tick;
        }

        public static void ClearForTests() => LastAdTick.Clear();
    }
}
