using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// The level-40 specialty commanders (issue #35; owner, 2026-11-17). Live's tooltips: Bone Magus (Darkness, a caster:
    /// snare, debuff or direct damage), Bone Keeper (Suppression: lifedrain or suppress) and Bone Captain (Bone Army: a
    /// bowman), each summoned at 85% of the caster's level for 20% power. The released world had only a "Summon Bone
    /// Magus" row (10105) in no spell line, and no Keeper or Captain at all. This adds them at level 40 of the three
    /// specialization lines, on the existing dread lich (212), dread guardian (213) and dread archer (211) templates,
    /// and gives the Keeper its missing suppress spells. Each commander controls three minions, like the Bone Master
    /// (the "150 combined levels" of live is about four of our level-37 minions; the owner chose three).
    /// Runs before the spells load, so the rows are in place on the first start; it only writes what is missing or
    /// differs, so later starts change nothing.
    /// </summary>
    public static class BonedancerSpecialtyCommanders
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const int LineLevel = 40;
        public const double PetLevelPercent = 85;
        public const int PowerPercent = 20;

        /// <summary>One commander: its summon spell, the line that teaches it, and the template it summons.</summary>
        public sealed record Commander(int SpellId, string Name, string Line, int TemplateId, string TemplateSpells);

        /// <summary>
        /// The Bone Keeper's template carried only its lifetap (60119). It gains Shroud of Cowardice (10085, a 50% snare)
        /// and Annihilate Strength (2658), both from the Bonedancer's own lines, for its [suppress] choice.
        /// </summary>
        public static readonly Commander[] Commanders =
        [
            new(10105, "Summon Bone Magus", "Bone Mystics", 212, null),
            new(10106, "Summon Bone Keeper", "Bone Guardians", 213, "60119;10085;2658"),
            new(10107, "Summon Bone Captain", "Bone Warriors", 211, null),
        ];

        public const string Description = "Summons a pet to serve the caster. Controls 3 additional pets.";

        /// <summary>Fills a summon spell row the way the stock commander summons are (Summon Bone Master, 10104).</summary>
        public static void Fill(DbSpell row, Commander commander)
        {
            row.SpellID = commander.SpellId;
            row.Name = commander.Name;
            row.Description = Description;
            row.Type = "SummonCommander";
            row.Target = "Self";
            row.Range = 0;
            row.Power = -PowerPercent;
            row.CastTime = 6.0;
            row.Damage = -PetLevelPercent;
            row.Value = 50;
            row.Duration = 65535;
            row.Concentration = 0;
            row.LifeDrainReturn = commander.TemplateId;
            row.ClientEffect = 10104;
            row.Icon = 10104;
            row.PackageID = "Bonedancer";
            row.TooltipId = (ushort)commander.SpellId;
        }

        /// <summary>True when the row already says what <see cref="Fill"/> would write.</summary>
        public static bool Matches(DbSpell row, Commander commander) =>
            row != null && row.SpellID == commander.SpellId && row.Name == commander.Name && row.Description == Description &&
            row.Type == "SummonCommander" && row.Power == -PowerPercent && Math.Abs(row.Damage + PetLevelPercent) < 0.001 &&
            row.LifeDrainReturn == commander.TemplateId && Math.Abs(row.CastTime - 6.0) < 0.001;

        public static void Ensure()
        {
            IObjectDatabase db = GameServer.Database;
            if (db == null)
                return;
            foreach (Commander commander in Commanders)
            {
                try
                {
                    if (db.SelectObject<DbNpcTemplate>(DB.Column("TemplateId").IsEqualTo(commander.TemplateId)) is not DbNpcTemplate template)
                    {
                        log.Warn($"Bonedancer specialty commanders: npc template {commander.TemplateId} is missing, {commander.Name} is not added.");
                        continue;
                    }
                    if (commander.TemplateSpells != null && template.Spells != commander.TemplateSpells)
                    {
                        template.Spells = commander.TemplateSpells;
                        db.SaveObject(template);
                    }

                    DbSpell spell = db.SelectObject<DbSpell>(DB.Column("SpellID").IsEqualTo(commander.SpellId));
                    if (spell == null)
                    {
                        spell = new DbSpell { ObjectId = commander.SpellId.ToString() };
                        Fill(spell, commander);
                        db.AddObject(spell);
                    }
                    else if (!Matches(spell, commander))
                    {
                        Fill(spell, commander);
                        db.SaveObject(spell);
                    }

                    IList<DbLineXSpell> taught = db.SelectObjects<DbLineXSpell>(DB.Column("SpellID").IsEqualTo(commander.SpellId));
                    if (!taught.Any(row => row.LineName == commander.Line && row.Level == LineLevel))
                        db.AddObject(new DbLineXSpell { LineName = commander.Line, SpellID = commander.SpellId, Level = LineLevel });
                }
                catch (Exception e)
                {
                    log.Warn($"Bonedancer specialty commanders: could not add {commander.Name}: {e.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Spell to-hit for Bonedancer pets (issue #35; owner, 2026-11-17). A pet's spell loses half a point of to-hit per
    /// level below its target, and one more as an NPC caster, so a level-42 commander was resisted about 24% of the time
    /// by a level-50 player, twice a player's 12.5%. A commander gets back the whole gap to its Bonedancer's level, so it
    /// lands spells like its owner would. A minion gets back half, so it still misses more often than its commander:
    /// four pets on one player should not drop that player at once.
    /// </summary>
    public static class BonedancerPetAccuracy
    {
        /// <summary>To-hit points per level of gap that an NPC caster loses (half a point as a caster, one as an NPC).</summary>
        public const double PointsPerLevel = 1.5;
        public const double CommanderShare = 1.0;
        public const double MinionShare = 0.5;

        public static double Bonus(bool commander, int ownerLevel, int petLevel)
        {
            int gap = ownerLevel - petLevel;
            return gap <= 0 ? 0 : gap * PointsPerLevel * (commander ? CommanderShare : MinionShare);
        }

        /// <summary>The bonus for this caster, 0 for anyone but a Bonedancer's commander or minion.</summary>
        public static double SpellBonus(GameLiving caster) => caster switch
        {
            CommanderPet commander when commander.Owner is GameLiving owner => Bonus(true, owner.Level, commander.Level),
            BdSubPet minion when minion.RootOwner is GameLiving root => Bonus(false, root.Level, minion.Level),
            _ => 0,
        };
    }
}
