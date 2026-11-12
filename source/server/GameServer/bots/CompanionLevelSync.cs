using System;

namespace DOL.GS
{
    /// <summary>
    /// Rules for /spawn helpers following their owner's level while grouped with them.
    /// The level change itself is applied by GameBot.TryMatchOwnerLevel on the
    /// bot's own idle turn. Autonomous gamebots never use these rules.
    /// </summary>
    public static class CompanionLevelSync
    {
        /// <summary>The level a companion should be at: its owner's, but never lower than it already is.</summary>
        public static byte TargetLevel(int ownerLevel, int botLevel, int maxLevel)
        {
            int target = Math.Clamp(ownerLevel, 1, Math.Max(1, maxLevel));
            return (byte)Math.Clamp(Math.Max(botLevel, target), 1, Math.Max(1, maxLevel));
        }

        /// <summary>Only a companion in its owner's current group follows the owner's level.</summary>
        public static bool InOwnersGroup(object botGroup, object ownerGroup) =>
            botGroup != null && ReferenceEquals(botGroup, ownerGroup);

        /// <summary>
        /// The kit level to generate after a level sync. A /spawn helper rolls in the same
        /// level-10..level band it spawned with (the full endgame kit at 50) but never below
        /// the kit it already wore. A non-temporary bot is equipped at its level.
        /// </summary>
        public static int GearLevel(bool temporary, int newLevel, int previousGearLevel, double roll)
        {
            if (!temporary || TemporaryCompanionBalance.IsEndgame(true, newLevel))
                return newLevel;
            int floor = Math.Max(1, newLevel - 10);
            int span = newLevel - floor + 1;
            int rolled = floor + Math.Clamp((int)(Math.Clamp(roll, 0, 0.999999) * span), 0, span - 1);
            return Math.Clamp(Math.Max(rolled, previousGearLevel), floor, newLevel);
        }
    }
}
