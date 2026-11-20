using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// How well a piece of gear's magic bonuses suit a class. Group loot rolls use it so a bot rolls
    /// Need only on gear made for it (a Minstrel on Charisma and Instruments, not on +Staff), and so
    /// the member whose class gets the most from a piece wins it when several roll Need.
    ///
    /// Stats come from the endgame template priorities of each class (the table below). Skills and
    /// focus come from the class's own specialization lines, weighted up by how far the member has
    /// trained them. A bonus the class cannot use (another class's skill, a stat it never needs)
    /// counts toward the piece's total but not toward its fit.
    /// </summary>
    public static class LootClassFit
    {
        /// <summary>A bot rolls Need on a piece with bonuses only when at least this share of them suits its class.</summary>
        public const double MinimumShareForNeed = 0.5;

        /// <summary>Two Need rolls whose fit is within this share of each other are a tie, and the die decides.</summary>
        public const double TieShare = 0.9;

        /// <summary>
        /// Stat priorities by class, from the established endgame template meta. 3 = the class's main
        /// stat (the one it caps first), 2 = an important stat, 1 = useful. A stat not listed is worth
        /// nothing to the class. The mana stat also gives Acuity its weight.
        /// Casters and healers: their casting stat and Dexterity (cast speed) first, then Constitution.
        /// Tanks: Strength, Constitution, Quickness. Dual wielders and assassins: Strength and Quickness,
        /// with Dexterity for their offhand. Archers: Dexterity first. Hybrids add their casting stat.
        /// </summary>
        private static readonly Dictionary<eCharacterClass, string> StatPriorities = new()
        {
            // Albion
            [eCharacterClass.Armsman] = "STR3 CON3 QUI2 DEX1",
            [eCharacterClass.Mercenary] = "STR3 QUI3 CON2 DEX2",
            [eCharacterClass.Paladin] = "STR3 CON3 QUI2 DEX1 PIE1",
            [eCharacterClass.Reaver] = "STR3 CON3 QUI2 PIE2 DEX1",
            [eCharacterClass.Infiltrator] = "DEX3 QUI3 STR2 CON2",
            [eCharacterClass.Scout] = "DEX3 QUI2 CON2 STR1",
            [eCharacterClass.Minstrel] = "CHR3 DEX2 QUI2 CON2 STR1",
            [eCharacterClass.Friar] = "STR3 PIE2 DEX2 QUI2 CON2",
            [eCharacterClass.Cleric] = "PIE3 DEX3 CON2",
            [eCharacterClass.Heretic] = "PIE3 DEX2 CON2 STR2 QUI1",
            [eCharacterClass.Cabalist] = "INT3 DEX3 CON2",
            [eCharacterClass.Necromancer] = "INT3 DEX2 CON2",
            [eCharacterClass.Sorcerer] = "INT3 DEX3 CON2",
            [eCharacterClass.Theurgist] = "INT3 DEX3 CON2",
            [eCharacterClass.Wizard] = "INT3 DEX3 CON2",
            [eCharacterClass.MaulerAlb] = "STR3 QUI2 CON2 DEX1",
            // Midgard
            [eCharacterClass.Warrior] = "STR3 CON3 QUI2 DEX1",
            [eCharacterClass.Berserker] = "STR3 QUI3 CON2 DEX2",
            [eCharacterClass.Savage] = "STR3 QUI3 CON2 DEX2",
            [eCharacterClass.Thane] = "STR3 CON2 QUI2 PIE2 DEX1",
            [eCharacterClass.Skald] = "STR3 CON2 QUI2 CHR2 DEX1",
            [eCharacterClass.Valkyrie] = "STR3 CON2 QUI2 PIE2 DEX1",
            [eCharacterClass.Shadowblade] = "STR3 QUI3 DEX2 CON2",
            [eCharacterClass.Hunter] = "DEX3 QUI2 STR2 CON2",
            [eCharacterClass.Healer] = "PIE3 DEX3 CON2",
            [eCharacterClass.Shaman] = "PIE3 DEX3 CON2",
            [eCharacterClass.Runemaster] = "PIE3 DEX3 CON2",
            [eCharacterClass.Spiritmaster] = "PIE3 DEX3 CON2",
            [eCharacterClass.Bonedancer] = "PIE3 DEX2 CON2",
            [eCharacterClass.Warlock] = "PIE3 DEX3 CON2",
            [eCharacterClass.MaulerMid] = "STR3 QUI2 CON2 DEX1",
            // Hibernia
            [eCharacterClass.Hero] = "STR3 CON3 QUI2 DEX1",
            [eCharacterClass.Blademaster] = "STR3 QUI3 CON2 DEX2",
            [eCharacterClass.Champion] = "STR3 CON2 QUI2 INT2 DEX1",
            [eCharacterClass.Valewalker] = "STR3 INT2 QUI2 CON2 DEX1",
            [eCharacterClass.Warden] = "EMP3 STR2 CON2 DEX2 QUI1",
            [eCharacterClass.Nightshade] = "DEX3 QUI3 STR2 CON2",
            [eCharacterClass.Ranger] = "DEX3 QUI2 STR2 CON2",
            [eCharacterClass.Vampiir] = "STR3 CON2 DEX2 QUI2",
            [eCharacterClass.Bard] = "CHR3 DEX3 CON2 QUI1",
            [eCharacterClass.Druid] = "EMP3 DEX3 CON2",
            [eCharacterClass.Animist] = "INT3 DEX2 CON2",
            [eCharacterClass.Bainshee] = "INT3 DEX3 CON2",
            [eCharacterClass.Eldritch] = "INT3 DEX3 CON2",
            [eCharacterClass.Enchanter] = "INT3 DEX3 CON2",
            [eCharacterClass.Mentalist] = "INT3 DEX3 CON2",
            [eCharacterClass.MaulerHib] = "STR3 QUI2 CON2 DEX1",
        };

        private static readonly Dictionary<eCharacterClass, Dictionary<eStat, double>> ParsedPriorities =
            StatPriorities.ToDictionary(static entry => entry.Key, static entry => ParsePriorities(entry.Value));

        /// <summary>The class's skill lines and the member's trained level in each. Tests replace it.</summary>
        public static Func<GameLiving, IEnumerable<(string Spec, int Level)>> SpecsOf { get; set; } = DefaultSpecs;

        private const long ProfileLifetimeMs = 60_000;
        private static readonly ConditionalWeakTable<GameLiving, CachedProfile> Profiles = new();

        private sealed class CachedProfile
        {
            public Profile Profile;
            public long BuiltAt;
            public int ClassId;
            public int Level;
        }

        /// <summary>What each bonus is worth to one member.</summary>
        public sealed class Profile
        {
            public readonly Dictionary<eProperty, double> Weights = new();
            public bool Casts;
            public bool MeleeWeapons;
            public bool MagicSkills;
            public bool DualWield;
            public bool Archery;

            public double WeightOf(eProperty property) => Weights.TryGetValue(property, out double weight) ? weight : 0;
        }

        /// <summary>
        /// Score: the bonuses weighted by how much the class wants each one; the higher, the better
        /// the piece suits the member. Useful and Total: the bonus value the class can use, and all of it.
        /// </summary>
        public readonly record struct Fit(double Score, double Useful, double Total)
        {
            public bool HasBonuses => Total > 0;
            /// <summary>The share of the piece's bonuses the class can use. A piece without bonuses suits everyone.</summary>
            public double Share => Total > 0 ? Useful / Total : 1;
            public bool SuitsClass => !HasBonuses || Share >= MinimumShareForNeed;
        }

        public static Fit Of(GameLiving living, DbInventoryItem item)
        {
            if (living == null || item == null)
                return default;
            Profile profile = ProfileOf(living);
            return profile == null ? default : Score(profile, item);
        }

        public static Fit Score(Profile profile, DbInventoryItem item)
        {
            if (profile == null || item == null)
                return default;

            double score = 0, useful = 0, total = 0;
            foreach ((int amount, int type) in Bonuses(item))
            {
                if (amount <= 0 || type <= 0)
                    continue;
                eProperty property = (eProperty)type;
                double value = amount * ValuePerPoint(property);
                bool neutral = IsNeutral(property);
                double weight = neutral ? 0.5 : profile.WeightOf(property);
                total += value;
                if (weight <= 0)
                    continue;
                useful += value;
                score += value * weight;
            }

            return new Fit(score, useful, total);
        }

        public static Profile ProfileOf(GameLiving living)
        {
            ICharacterClass characterClass = ClassOf(living);
            if (characterClass == null)
                return null;

            long now = Environment.TickCount64;
            if (Profiles.TryGetValue(living, out CachedProfile cached) && cached.ClassId == characterClass.ID &&
                cached.Level == living.Level && now - cached.BuiltAt < ProfileLifetimeMs)
                return cached.Profile;

            Profile profile = Build(characterClass, living.Level, SafeSpecs(living));
            Profiles.AddOrUpdate(living, new CachedProfile { Profile = profile, BuiltAt = now, ClassId = characterClass.ID, Level = living.Level });
            return profile;
        }

        /// <summary>Players and bots have a class. Other creatures (pets, mobs) do not roll for loot.</summary>
        public static ICharacterClass ClassOf(GameLiving living) => living switch
        {
            GamePlayer player => player.CharacterClass,
            GameBot bot => bot.CharacterClass,
            _ => null
        };

        /// <summary>Forgets cached profiles. For tests.</summary>
        public static void Reset() => Profiles.Clear();

        public static Profile Build(ICharacterClass characterClass, int level, IEnumerable<(string Spec, int Level)> specs)
        {
            Profile profile = new();
            if (characterClass == null)
                return profile;

            Dictionary<eStat, double> stats = StatsFor(characterClass);
            foreach ((eStat stat, double weight) in stats)
            {
                profile.Weights[(eProperty)stat] = weight;
                profile.Weights[StatCap(stat)] = weight;
                profile.Weights[MythicalStatCap(stat)] = weight;
            }

            eStat manaStat = characterClass.ManaStat;
            if ((manaStat is eStat.INT or eStat.PIE or eStat.EMP or eStat.CHR) && stats.TryGetValue(manaStat, out double manaWeight))
            {
                profile.Weights[eProperty.Acuity] = manaWeight;
                profile.Weights[eProperty.AcuCapBonus] = manaWeight;
                profile.Weights[eProperty.MythicalAcuCapBonus] = manaWeight;
                profile.Casts = manaWeight >= 2;
            }

            // Hit points and resists help every class.
            profile.Weights[eProperty.MaxHealth] = 1;
            profile.Weights[eProperty.MaxHealthCapBonus] = 1;
            for (eProperty resist = eProperty.Resist_First; resist <= eProperty.Resist_Last; resist++)
                profile.Weights[resist] = 1;
            for (eProperty cap = eProperty.ResCapBonus_First; cap <= eProperty.ResCapBonus_Last; cap++)
                profile.Weights[cap] = 1;
            if (profile.Casts)
            {
                profile.Weights[eProperty.MaxMana] = 1;
                profile.Weights[eProperty.PowerPool] = 1;
                profile.Weights[eProperty.PowerPoolCapBonus] = 1;
            }

            foreach ((string spec, int specLevel) in specs ?? Enumerable.Empty<(string, int)>())
            {
                if (string.IsNullOrEmpty(spec))
                    continue;
                // A line the class has is worth 1, rising to 2 as the member trains it up to its level.
                double weight = 1 + Math.Clamp(specLevel / (double)Math.Max(1, level), 0, 1);
                eProperty skill = SkillBase.SpecToSkill(spec);
                if (skill != eProperty.Undefined)
                {
                    profile.Weights[skill] = Math.Max(profile.WeightOf(skill), weight);
                    profile.MeleeWeapons |= SkillBase.CheckPropertyType(skill, ePropertyType.SkillMeleeWeapon);
                    profile.MagicSkills |= SkillBase.CheckPropertyType(skill, ePropertyType.SkillMagical);
                    profile.DualWield |= SkillBase.CheckPropertyType(skill, ePropertyType.SkillDualWield);
                    profile.Archery |= SkillBase.CheckPropertyType(skill, ePropertyType.SkillArchery);
                }

                eProperty focus = SkillBase.SpecToFocus(spec);
                if (focus != eProperty.Undefined)
                    profile.Weights[focus] = Math.Max(profile.WeightOf(focus), weight);
            }

            profile.Weights[eProperty.AllSkills] = 1;
            if (profile.MagicSkills)
                profile.Weights[eProperty.AllMagicSkills] = 1;
            if (profile.MeleeWeapons)
                profile.Weights[eProperty.AllMeleeWeaponSkills] = 1;
            if (profile.DualWield)
                profile.Weights[eProperty.AllDualWieldingSkills] = 1;
            if (profile.Archery)
                profile.Weights[eProperty.AllArcherySkills] = 1;
            return profile;
        }

        /// <summary>True when the template table lists this class.</summary>
        public static bool HasPriorities(eCharacterClass characterClass) => ParsedPriorities.ContainsKey(characterClass);

        /// <summary>The class's template priorities, or its own primary, secondary and tertiary stats for a class the table lacks.</summary>
        public static Dictionary<eStat, double> StatsFor(ICharacterClass characterClass)
        {
            if (characterClass != null && ParsedPriorities.TryGetValue((eCharacterClass)characterClass.ID, out Dictionary<eStat, double> known))
                return known;

            Dictionary<eStat, double> stats = new();
            if (characterClass == null)
                return stats;
            void Add(eStat stat, double weight)
            {
                if (stat is >= eStat.STR and <= eStat.CHR)
                    stats[stat] = Math.Max(stats.GetValueOrDefault(stat), weight);
            }
            Add(characterClass.PrimaryStat, 3);
            Add(characterClass.SecondaryStat, 2);
            Add(characterClass.TertiaryStat, 1);
            Add(characterClass.ManaStat, 3);
            Add(eStat.CON, 2);
            return stats;
        }

        /// <summary>
        /// Puts different bonuses on one scale, close to their spellcraft imbue cost:
        /// 1 stat point = 1, 1 skill level = 5, 1% resist = 2, 4 hit points = 1.
        /// </summary>
        public static double ValuePerPoint(eProperty property)
        {
            if (property is >= eProperty.Stat_First and <= eProperty.Stat_Last || property == eProperty.Acuity)
                return 1;
            if (property is >= eProperty.StatCapBonus_First and <= eProperty.AcuCapBonus ||
                property is >= eProperty.MythicalStatCapBonus_First and <= eProperty.MythicalStatCapBonus_Last)
                return 2;
            if (property == eProperty.MaxHealth)
                return 0.25;
            if (property == eProperty.MaxHealthCapBonus)
                return 0.5;
            if (property is eProperty.MaxMana or eProperty.PowerPool or eProperty.PowerPoolCapBonus)
                return 2;
            if (property is >= eProperty.Resist_First and <= eProperty.Resist_Last ||
                property is >= eProperty.ResCapBonus_First and <= eProperty.ResCapBonus_Last)
                return 2;
            if (property is >= eProperty.Skill_First and <= eProperty.Skill_Last ||
                property is eProperty.AllSkills or eProperty.AllMagicSkills or eProperty.AllMeleeWeaponSkills
                    or eProperty.AllDualWieldingSkills or eProperty.AllArcherySkills)
                return 5;
            if (property is >= eProperty.Focus_Darkness and <= eProperty.Focus_Arboreal)
                return 0.2;
            return 1;
        }

        /// <summary>A bonus this table does not judge (armor factor, speed and the like) suits everyone at half weight.</summary>
        private static bool IsNeutral(eProperty property) =>
            !(property is >= eProperty.Stat_First and <= eProperty.Resist_Last ||
              property is >= eProperty.Skill_First and <= eProperty.Skill_Last ||
              property is >= eProperty.Focus_Darkness and <= eProperty.Focus_Arboreal ||
              property is >= eProperty.StatCapBonus_First and <= eProperty.StatCapBonus_Last ||
              property is >= eProperty.ResCapBonus_First and <= eProperty.ResCapBonus_Last ||
              property is >= eProperty.MythicalStatCapBonus_First and <= eProperty.MythicalStatCapBonus_Last ||
              property is eProperty.Acuity or eProperty.PowerPool or eProperty.AllSkills or eProperty.AllMagicSkills
                  or eProperty.AllMeleeWeaponSkills or eProperty.AllDualWieldingSkills or eProperty.AllArcherySkills);

        public static IEnumerable<(int Amount, int Type)> Bonuses(DbInventoryItem item)
        {
            yield return (item.Bonus1, item.Bonus1Type);
            yield return (item.Bonus2, item.Bonus2Type);
            yield return (item.Bonus3, item.Bonus3Type);
            yield return (item.Bonus4, item.Bonus4Type);
            yield return (item.Bonus5, item.Bonus5Type);
            yield return (item.Bonus6, item.Bonus6Type);
            yield return (item.Bonus7, item.Bonus7Type);
            yield return (item.Bonus8, item.Bonus8Type);
            yield return (item.Bonus9, item.Bonus9Type);
            yield return (item.Bonus10, item.Bonus10Type);
            yield return (item.ExtraBonus, item.ExtraBonusType);
        }

        private static eProperty StatCap(eStat stat) => eProperty.StatCapBonus_First + ((int)stat - (int)eStat.STR);
        private static eProperty MythicalStatCap(eStat stat) => eProperty.MythicalStatCapBonus_First + ((int)stat - (int)eStat.STR);

        private static Dictionary<eStat, double> ParsePriorities(string text)
        {
            Dictionary<eStat, double> stats = new();
            foreach (string token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                stats[Enum.Parse<eStat>(token[..^1])] = token[^1] - '0';
            return stats;
        }

        private static IEnumerable<(string Spec, int Level)> SafeSpecs(GameLiving living)
        {
            try
            {
                return (SpecsOf ?? DefaultSpecs)(living)?.ToList() ?? new List<(string, int)>();
            }
            catch (Exception)
            {
                // A member whose skills cannot be read is judged on stats alone.
                return new List<(string, int)>();
            }
        }

        private static IEnumerable<(string Spec, int Level)> DefaultSpecs(GameLiving living)
        {
            Dictionary<string, int> specs = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Specialization spec in SkillBase.GetSpecializationCareer(ClassOf(living).ID).Keys)
                {
                    if (spec?.KeyName != null)
                        specs[spec.KeyName] = 1;
                }
            }
            catch (Exception)
            {
                // No career table (tests, or a class without one): the member's own lines still count.
            }

            try
            {
                IList<Specialization> trained = living switch
                {
                    GamePlayer player => player.GetSpecList(),
                    GameBot bot => bot.GetSpecList(),
                    _ => null
                };
                foreach (Specialization spec in trained ?? Array.Empty<Specialization>())
                {
                    if (spec?.KeyName != null)
                        specs[spec.KeyName] = Math.Max(specs.GetValueOrDefault(spec.KeyName), spec.Level);
                }
            }
            catch (Exception)
            {
            }

            return specs.Select(static entry => (entry.Key, entry.Value));
        }
    }
}
