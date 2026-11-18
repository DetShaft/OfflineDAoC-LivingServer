namespace DOL.GS
{
    /// <summary>
    /// Melee bots with instant spells (Reaver lifedrains and DoTs, and the like) keep swinging when they throw one.
    /// Issue #126 (Jay, 2026-11-18, Killaloe): a Reaver in his group ran back and forth at an enemy player and then
    /// stood next to it without hitting. Each think it cast an instant spell (its lifedrain has no recast delay), and
    /// the combat turn answered every spell action with StopAttack, so the swing never landed and the melee follow was
    /// dropped. An instant spell needs no stop in DAoC; only a real cast with a cast time does.
    /// The Savage (SavageBotCombatPolicy) and the Minstrel (MinstrelBotCombatPolicy) had their own versions of this.
    /// </summary>
    public static class BotHybridMeleePolicy
    {
        private static bool IsInstant(Spell spell) => spell != null && spell.IsInstantCast;

        /// <summary>
        /// True when a melee bot's spell action this turn was only instant spells, so its melee swing and chase go on.
        /// A cast with a cast time (running or queued) still stops the swing, as do unknown queued requests.
        /// </summary>
        public static bool ContinueMeleeAfterInstant(bool meleeBot, bool spellAction,
            bool isCasting, Spell activeSpell, bool hasPendingCast, Spell pendingSpell)
        {
            if (!meleeBot || !spellAction)
                return false;
            if (isCasting && !IsInstant(activeSpell))
                return false;
            if (hasPendingCast && !IsInstant(pendingSpell))
                return false;
            return true;
        }
    }
}
