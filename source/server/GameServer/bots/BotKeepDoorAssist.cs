namespace DOL.GS
{
    /// <summary>
    /// Which group bots help the player break an enemy keep door (#133). Spells have no effect on doors
    /// (SpellHandler.IsTargetAffectedBySpellType), so only classes that fight with a weapon join in: tanks, melee and
    /// archers. List casters (except the Valewalker, who fights with a scythe), pure healers, Shamans and Bards hold back.
    /// The rule reads class types only, so it is the same for all three realms.
    /// </summary>
    public static class BotKeepDoorAssist
    {
        public static bool HitsDoors(GameBot bot) =>
            bot?.CharacterClass != null && HitsDoors((eCharacterClass)bot.CharacterClass.ID, bot.CharacterClass.ClassType);

        public static bool HitsDoors(eCharacterClass characterClass, eClassType classType)
        {
            if (characterClass == eCharacterClass.Valewalker)
                return true;
            if (classType == eClassType.ListCaster)
                return false;
            return !BotPartyRoles.HoldsBackLine(characterClass);
        }
    }
}
