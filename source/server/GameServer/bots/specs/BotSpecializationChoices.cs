using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// Spec variants a bot class can be created with. Linked into the launcher so create-time pickers match
    /// <see cref="BotSpec.ChoosePersistentSpecialization"/> without pulling the full BotSpec graph.
    /// </summary>
    public static class BotSpecializationChoices
    {
        /// <summary>Sentinel written by the launcher when the player leaves Spec on Auto (hashed botId).</summary>
        public const int AutoPreferredSpec = -1;

        public static IReadOnlyList<eSpecType> For(eCharacterClass charClass) => charClass switch
        {
            eCharacterClass.Armsman => [eSpecType.OneHandAndShield, eSpecType.OneHandHybrid, eSpecType.TwoHandHybrid, eSpecType.TwoHanded],
            eCharacterClass.Cabalist => [eSpecType.MatterCab, eSpecType.BodyCab, eSpecType.SpiritCab],
            eCharacterClass.Cleric => [eSpecType.RejuvCleric, eSpecType.EnhanceCleric, eSpecType.SmiteCleric],
            eCharacterClass.Friar => [eSpecType.RejuvFriar, eSpecType.EnhanceFriar, eSpecType.StaffFriar],
            eCharacterClass.Mercenary => [eSpecType.DualWield, eSpecType.DualWieldAndShield],
            eCharacterClass.Paladin => [eSpecType.OneHandAndShield, eSpecType.TwoHandHybrid],
            eCharacterClass.Sorcerer => [eSpecType.MatterSorc, eSpecType.BodySorc, eSpecType.MindSorc],
            eCharacterClass.Theurgist => [eSpecType.EarthTheur, eSpecType.IceTheur, eSpecType.AirTheur],
            eCharacterClass.Wizard => [eSpecType.EarthWiz, eSpecType.IceWiz, eSpecType.FireWiz],
            eCharacterClass.Necromancer => [eSpecType.DeathsightNecro, eSpecType.PainworkingNecro],
            eCharacterClass.Blademaster => [eSpecType.DualWield, eSpecType.DualWieldAndShield],
            eCharacterClass.Champion => [eSpecType.OneHandAndShield, eSpecType.TwoHanded],
            eCharacterClass.Druid => [eSpecType.RegrowthDruid, eSpecType.NurtureDruid, eSpecType.NatureDruid],
            eCharacterClass.Eldritch => [eSpecType.LightEld, eSpecType.ManaEld, eSpecType.VoidEld],
            eCharacterClass.Enchanter => [eSpecType.LightEnchanter, eSpecType.ManaEnchanter, eSpecType.EnchantmentEnchanter],
            eCharacterClass.Hero => [eSpecType.OneHandAndShield, eSpecType.TwoHandHybrid],
            eCharacterClass.Mentalist => [eSpecType.LightMenta, eSpecType.ManaMenta, eSpecType.MentaMenta],
            eCharacterClass.Animist => [eSpecType.ArborealAnimist, eSpecType.CreepingAnimist, eSpecType.VerdantAnimist],
            eCharacterClass.Warden => [eSpecType.RegrowthWarden, eSpecType.NurtureWarden, eSpecType.BattleWarden],
            eCharacterClass.Sluaghbinder => [eSpecType.SluaghbinderBulwark, eSpecType.SluaghbinderBane, eSpecType.SluaghbinderCovenant],
            eCharacterClass.Bonedancer => [eSpecType.DarkBone, eSpecType.SuppBone, eSpecType.ArmyBone],
            eCharacterClass.Healer => [eSpecType.MendHealer, eSpecType.AugHealer, eSpecType.PacHealer],
            eCharacterClass.Runemaster => [eSpecType.DarkRune, eSpecType.SuppRune, eSpecType.RuneRune],
            eCharacterClass.Savage => [eSpecType.TwoHanded, eSpecType.DualWield],
            eCharacterClass.Shadowblade => [eSpecType.LeftAxe, eSpecType.TwoHanded],
            eCharacterClass.Shaman => [eSpecType.MendShaman, eSpecType.AugShaman, eSpecType.SubtShaman],
            eCharacterClass.Spiritmaster => [eSpecType.DarkSpirit, eSpecType.SuppSpirit, eSpecType.SummSpirit],
            eCharacterClass.Thane => [eSpecType.OneHandAndShield, eSpecType.TwoHanded],
            eCharacterClass.Skald => [eSpecType.OneHandAndShield, eSpecType.TwoHanded],
            eCharacterClass.Warrior => [eSpecType.OneHandAndShield, eSpecType.TwoHanded],
            _ => [eSpecType.None],
        };

        public static bool HasChoices(eCharacterClass charClass)
        {
            IReadOnlyList<eSpecType> choices = For(charClass);
            return choices.Count > 1 || (choices.Count == 1 && choices[0] != eSpecType.None);
        }

        public static string DisplayName(eSpecType spec) => spec switch
        {
            eSpecType.None => "Default",
            eSpecType.MatterCab => "Matter",
            eSpecType.BodyCab => "Body",
            eSpecType.SpiritCab => "Spirit",
            eSpecType.RejuvCleric => "Rejuvenation",
            eSpecType.EnhanceCleric => "Enhancement",
            eSpecType.SmiteCleric => "Smite",
            eSpecType.RejuvFriar => "Rejuvenation",
            eSpecType.EnhanceFriar => "Enhancement",
            eSpecType.StaffFriar => "Staff",
            eSpecType.MatterSorc => "Matter",
            eSpecType.BodySorc => "Body",
            eSpecType.MindSorc => "Mind",
            eSpecType.EarthTheur => "Earth",
            eSpecType.IceTheur => "Ice",
            eSpecType.AirTheur => "Air",
            eSpecType.EarthWiz => "Earth",
            eSpecType.IceWiz => "Ice",
            eSpecType.FireWiz => "Fire",
            eSpecType.DeathsightNecro => "Deathsight",
            eSpecType.PainworkingNecro => "Painworking",
            eSpecType.RegrowthDruid => "Regrowth",
            eSpecType.NurtureDruid => "Nurture",
            eSpecType.NatureDruid => "Nature",
            eSpecType.LightEld => "Light",
            eSpecType.ManaEld => "Mana",
            eSpecType.VoidEld => "Void",
            eSpecType.LightEnchanter => "Light",
            eSpecType.ManaEnchanter => "Mana",
            eSpecType.EnchantmentEnchanter => "Enchantment",
            eSpecType.LightMenta => "Light",
            eSpecType.ManaMenta => "Mana",
            eSpecType.MentaMenta => "Mentalism",
            eSpecType.ArborealAnimist => "Arboreal",
            eSpecType.CreepingAnimist => "Creeping Path",
            eSpecType.VerdantAnimist => "Verdant Path",
            eSpecType.RegrowthWarden => "Regrowth",
            eSpecType.NurtureWarden => "Nurture",
            eSpecType.BattleWarden => "Valor",
            eSpecType.DarkSpirit => "Darkness",
            eSpecType.SuppSpirit => "Suppression",
            eSpecType.SummSpirit => "Summoning",
            eSpecType.MendShaman => "Mending",
            eSpecType.AugShaman => "Augmentation",
            eSpecType.SubtShaman => "Cave Magic",
            eSpecType.DarkRune => "Darkness",
            eSpecType.SuppRune => "Suppression",
            eSpecType.RuneRune => "Runecarving",
            eSpecType.MendHealer => "Mending",
            eSpecType.AugHealer => "Augmentation",
            eSpecType.PacHealer => "Pacification",
            eSpecType.DarkBone => "Darkness",
            eSpecType.SuppBone => "Suppression",
            eSpecType.ArmyBone => "Bone Army",
            eSpecType.OneHanded => "One-handed",
            eSpecType.TwoHanded => "Two-handed",
            eSpecType.DualWield => "Dual wield",
            eSpecType.LeftAxe => "Left axe",
            eSpecType.OneHandAndShield => "One-hand + shield",
            eSpecType.TwoHandAndShield => "Two-hand + shield",
            eSpecType.DualWieldAndShield => "Dual wield + shield",
            eSpecType.OneHandHybrid => "One-hand hybrid",
            eSpecType.TwoHandHybrid => "Two-hand hybrid",
            eSpecType.SluaghbinderBulwark => "Bulwark",
            eSpecType.SluaghbinderBane => "Bane",
            eSpecType.SluaghbinderCovenant => "Covenant",
            _ => spec.ToString(),
        };
    }
}
