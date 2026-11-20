namespace DOL.GS
{
    /// <summary>
    /// #178 (Jay, 2026-11-20): a Cleric bot smites single targets only. Its area smites hit every monster near the target,
    /// so in PvE a group's Cleric pulled extra mobs ("he's AoE pulling in PvE"). The rule holds in PvP too, by the owner's
    /// choice. Every Cleric bot: companions, /spawn helpers and autonomous gamebots alike.
    /// </summary>
    public static class ClericBotSpellPolicy
    {
        /// <summary>False for a Cleric's offensive spell with an area (radius above 0); true for every other class.</summary>
        public static bool AllowsOffense(eCharacterClass characterClass, int radius) =>
            characterClass != eCharacterClass.Cleric || radius <= 0;
    }
}
