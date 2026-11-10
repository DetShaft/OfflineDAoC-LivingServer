using System.Collections.Generic;

namespace DOL.GS
{
    public class BotSpec
    {
        public static string SpecName;
        public eObjectType WeaponOneType;
        public eObjectType WeaponTwoType;
        public eWeaponDamageType DamageType = 0;
        public eSpecType SpecType;
        public bool Is2H;
        public List<BotSpecLine> SpecLines = new List<BotSpecLine>();

        public BotSpec()
        { }

        protected void Add(string spec, uint cap, float ratio)
        {
            SpecLines.Add(new BotSpecLine(spec, cap, ratio));
        }

        protected string ObjToSpec(eObjectType obj)
        {
            return SkillBase.ObjectTypeToSpec(obj);
        }

        public static BotSpec GetSpec(eCharacterClass charClass, eSpecType spec = eSpecType.None)
        {
            switch (charClass)
            {
                case eCharacterClass.Armsman: return new ArmsmanBotSpec(spec);
                case eCharacterClass.Cabalist: return new CabalistBotSpec(spec);
                case eCharacterClass.Cleric: return new ClericBotSpec(spec);
                case eCharacterClass.Friar: return new FriarBotSpec(spec);
                case eCharacterClass.Infiltrator: return new InfiltratorBotSpec();
                case eCharacterClass.Mercenary: return new MercenaryBotSpec(spec);
                case eCharacterClass.Minstrel: return new MinstrelBotSpec();
                case eCharacterClass.Paladin: return new PaladinBotSpec(spec);
                case eCharacterClass.Reaver: return new ReaverBotSpec();
                case eCharacterClass.Scout: return new ScoutBotSpec();
                case eCharacterClass.Sorcerer: return new SorcererBotSpec(spec);
                case eCharacterClass.Theurgist: return new TheurgistBotSpec(spec);
                case eCharacterClass.Wizard: return new WizardBotSpec(spec);
                case eCharacterClass.Necromancer: return new NecromancerBotSpec(spec);

                case eCharacterClass.Bard: return new BardBotSpec();
                case eCharacterClass.Blademaster: return new BlademasterBotSpec(spec);
                case eCharacterClass.Champion: return new ChampionBotSpec(spec);
                case eCharacterClass.Druid: return new DruidBotSpec(spec);
                case eCharacterClass.Eldritch: return new EldritchBotSpec(spec);
                case eCharacterClass.Enchanter: return new EnchanterBotSpec(spec);
                case eCharacterClass.Hero: return new HeroBotSpec(spec);
                case eCharacterClass.Mentalist: return new MentalistBotSpec(spec);
                case eCharacterClass.Nightshade: return new NightshadeBotSpec();
                case eCharacterClass.Animist: return new AnimistBotSpec(spec);
                case eCharacterClass.Ranger: return new RangerBotSpec();
                case eCharacterClass.Valewalker: return new ValewalkerBotSpec();
                case eCharacterClass.Warden: return new WardenBotSpec(spec);
                case eCharacterClass.Sluaghbinder: return new SluaghbinderBotSpec(spec);

                case eCharacterClass.Berserker: return new BerserkerBotSpec();
                case eCharacterClass.Bonedancer: return new BonedancerBotSpec(spec);
                case eCharacterClass.Healer: return new HealerBotSpec(spec);
                case eCharacterClass.Hunter: return new HunterBotSpec();
                case eCharacterClass.Runemaster: return new RunemasterBotSpec(spec);
                case eCharacterClass.Savage: return new SavageBotSpec(spec);
                case eCharacterClass.Shadowblade: return new ShadowbladeBotSpec(spec);
                case eCharacterClass.Shaman: return new ShamanBotSpec(spec);
                case eCharacterClass.Skald: return new SkaldBotSpec(spec);
                case eCharacterClass.Spiritmaster: return new SpiritmasterBotSpec(spec);
                case eCharacterClass.Thane: return new ThaneBotSpec(spec);
                case eCharacterClass.Warrior: return new WarriorBotSpec(spec);
            }

            return null;
        }

        public static IReadOnlyList<eSpecType> GetSpecializationChoices(eCharacterClass charClass) =>
            BotSpecializationChoices.For(charClass);

        public static eSpecType ChooseRandomSpecialization(eCharacterClass charClass)
        {
            IReadOnlyList<eSpecType> choices = GetSpecializationChoices(charClass);
            return choices[Util.Random(choices.Count - 1)];
        }

        public static eSpecType ChoosePersistentSpecialization(eCharacterClass charClass, long botId)
        {
            IReadOnlyList<eSpecType> choices = GetSpecializationChoices(charClass);
            unchecked
            {
                ulong value = (ulong)botId + 0x9E3779B97F4A7C15UL + (ulong)(int)charClass * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return choices[(int)(value % (ulong)choices.Count)];
            }
        }

        /// <summary>
        /// Create-time preferred specialization from the launcher when set; otherwise the hashed persistent pick.
        /// <paramref name="preferredSpecType"/> of <see cref="BotSpecializationChoices.AutoPreferredSpec"/> keeps the hash.
        /// </summary>
        public static eSpecType ChooseSpecialization(eCharacterClass charClass, long botId, int preferredSpecType)
        {
            IReadOnlyList<eSpecType> choices = GetSpecializationChoices(charClass);
            if (preferredSpecType != BotSpecializationChoices.AutoPreferredSpec &&
                System.Enum.IsDefined(typeof(eSpecType), preferredSpecType))
            {
                var preferred = (eSpecType)preferredSpecType;
                for (int i = 0; i < choices.Count; i++)
                {
                    if (choices[i] == preferred && preferred != eSpecType.None)
                        return preferred;
                }
            }
            return ChoosePersistentSpecialization(charClass, botId);
        }
    }
}
