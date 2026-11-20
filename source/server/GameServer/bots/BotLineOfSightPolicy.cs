namespace DOL.GS
{
    /// <summary>
    /// #176: what a ranged bot does after a cast failed its line of sight check. Before, GameNPC.OnCastSpellLosCheckFail
    /// started a follow on the target, the next think's ranged-caster turn stopped it (BotBrain.AttackMostWanted stops
    /// any follow before casting), and the bot cast into the same ridge again: it stood, crept a step, and stood again
    /// until the target came back into view (Jay, v1.1.24 fight mode, 2026-11-20).
    /// </summary>
    public static class BotLineOfSightPolicy
    {
        /// <summary>A failed check keeps the bot walking this long, unless it sees the target sooner.</summary>
        public const int HoldMilliseconds = 4_000;

        /// <summary>The walk ends this close to the target: from here the bot casts again whatever the result.</summary>
        public const int ApproachDistance = 350;

        /// <summary>
        /// Keep walking toward the target: the last failed check was on this target, the hold has not run out, the
        /// target is still out of sight (navmesh line of sight; false when the zone has none) and not yet close.
        /// </summary>
        public static bool Walks(long now, long blockedUntil, bool sameTarget, bool inSightNow, int distance) =>
            sameTarget && now < blockedUntil && !inSightNow && distance > ApproachDistance;
    }
}
