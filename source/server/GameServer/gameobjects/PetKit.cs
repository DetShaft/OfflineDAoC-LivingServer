using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.PlayerClass;
using DOL.GS.Styles;
using W = DOL.GS.PetOrders.Weapon;

namespace DOL.GS
{
    /// <summary>
    /// The weapons and shield of a player-class pet (issue #35, step 2; owner, 2026-11-17). PetOrders says what each pet
    /// may choose; this puts it in the pet's hands.
    /// <list type="bullet">
    /// <item>No pet spawns with a shield or a two-handed weapon in its kit: a Spiritmaster pet that rolled a spear
    /// or a two-handed model keeps a one-handed sword, axe or hammer, and the Enchanter's Ally and Compatriot start
    /// with a one-handed blade.</item>
    /// <item>Taunting gives a pet that holds a one-handed weapon a shield (and a small block chance if it had none)
    /// plus the shield styles a fighter of its realm knows at the pet's level. Stopping takes them away again.
    /// A pet with no one-handed weapon taunts without one.</item>
    /// <item>The Necromancer's Reanimated Servant and Necroservant spawn with a thrust or slash weapon and a shield that
    /// they keep (issue #117).</item>
    /// </list>
    /// The Bonedancer commander keeps its own weapons (CommanderPet).
    /// </summary>
    public static class PetKit
    {
        /// <summary>Block chance a shielded pet gets when its template gives it none.</summary>
        public const byte ShieldBlockChance = 10;

        /// <summary>The model a pet shows for a chosen weapon (the plain realm weapons of the item table).</summary>
        public static int Model(W weapon) => weapon switch
        {
            W.Sword => 312,
            W.Axe => 315,
            W.Hammer => 322,
            W.Blade => 446,
            W.Mace => 450,
            W.Dagger => 456,
            _ => 0,
        };

        public static int ShieldModel(eRealm realm) => realm switch
        {
            eRealm.Midgard => 1054,
            eRealm.Hibernia => 1102,
            _ => 59,
        };

        /// <summary>The one-handed weapon a model shows, None for anything else (two-handers, spears, staves).</summary>
        public static W WeaponOfModel(int model) => model switch
        {
            310 or 311 or 312 or 313 => W.Sword,
            315 or 316 or 319 or 573 => W.Axe,
            320 or 321 or 322 or 323 or 324 => W.Hammer,
            444 or 445 or 446 or 447 or 473 => W.Blade,
            449 or 450 or 451 or 452 or 461 => W.Mace,
            454 or 455 or 456 or 457 or 472 => W.Dagger,
            _ => W.None,
        };

        /// <summary>The fighter whose shield styles a pet of this realm learns.</summary>
        public static eCharacterClass ShieldTeacher(eRealm realm) => realm switch
        {
            eRealm.Midgard => eCharacterClass.Warrior,
            eRealm.Hibernia => eCharacterClass.Hero,
            _ => eCharacterClass.Armsman,
        };

        /// <summary>Shield styles up to this level, from the realm's fighter. Tests pass their own style source.</summary>
        public static List<Style> ShieldStyles(int level, eRealm realm, Func<string, int, List<Style>> stylesOf = null)
            => LineStyles(Specs.Shields, ShieldTeacher(realm), level, stylesOf);

        /// <summary>One weapon line's styles up to this level, as this class learns them.</summary>
        public static List<Style> LineStyles(string spec, eCharacterClass teacher, int level, Func<string, int, List<Style>> stylesOf = null)
        {
            stylesOf ??= CommanderPet.StyleSource;
            List<Style> line = stylesOf?.Invoke(spec, (int)teacher);
            if (line == null)
                return new List<Style>();
            return line.Where(style => style != null && style.SpecLevelRequirement <= level)
                .OrderBy(style => style.SpecLevelRequirement).ThenBy(style => style.ID).ToList();
        }

        #region Necromancer servants

        // Issue #117 (owner, 2026-11-17): the spell tooltips say "Thrust & Shield Styles" for Call Reanimated Servant
        // and "Slash & Shield Styles" for Call Necroservant. These two servants spawn with a one-handed weapon of that
        // line and a small shield, and know that line's and the shield's styles up to their level, as an Armsman
        // learns them. The shield is theirs for good: taunting neither adds nor removes it.

        /// <summary>Gladius: a plain Albion thrust weapon.</summary>
        public const int ServantThrustModel = 30;

        /// <summary>Short sword: a plain Albion slashing weapon.</summary>
        public const int ServantSlashModel = 3;

        /// <summary>The Albion fighter whose styles the servants learn.</summary>
        public const eCharacterClass ServantStyleTeacher = eCharacterClass.Armsman;

        /// <summary>The weapon line a Necromancer servant fights with, null for the servants that carry no shield kit.</summary>
        public static string ServantWeaponSpec(string petName) => (petName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "reanimated servant" => Specs.Thrust,
            "necroservant" => Specs.Slash,
            _ => null,
        };

        public static int ServantWeaponModel(string spec) => spec == Specs.Thrust ? ServantThrustModel : ServantSlashModel;

        /// <summary>The weapon line's and the shield's styles a servant knows at this level.</summary>
        public static List<Style> ServantStyles(string spec, int level, Func<string, int, List<Style>> stylesOf = null)
        {
            var styles = new List<Style>();
            if (string.IsNullOrEmpty(spec))
                return styles;
            var seen = new HashSet<int>();
            foreach (Style style in LineStyles(spec, ServantStyleTeacher, level, stylesOf)
                .Concat(LineStyles(Specs.Shields, ServantStyleTeacher, level, stylesOf)))
            {
                if (seen.Add(style.ID))
                    styles.Add(style);
            }
            return styles;
        }

        /// <summary>True for a servant that keeps its shield whatever its taunt.</summary>
        public static bool KeepsShield(GameSummonedPet pet) => pet is NecromancerPet && ServantWeaponSpec(pet.Name) != null;

        private static void ArmServant(GameSummonedPet pet, string spec)
        {
            DbInventoryItem right = pet.Inventory?.GetItem(eInventorySlot.RightHandWeapon);
            Equip(pet, ServantWeaponModel(spec), ShieldModel(eRealm.Albion), right?.Effect ?? 0);
            if (pet.BlockChance < ShieldBlockChance)
                pet.BlockChance = ShieldBlockChance;
            var styles = new List<Style>(pet.Styles ?? new List<Style>());
            var have = new HashSet<int>(styles.Where(style => style != null).Select(style => style.ID));
            styles.AddRange(ServantStyles(spec, pet.Level).Where(style => have.Add(style.ID)));
            pet.Styles = styles;
        }

        #endregion

        #region Live

        private sealed class Saved
        {
            public List<Style> Styles;
            public byte BlockChance;
        }

        private static readonly ConditionalWeakTable<GameSummonedPet, Saved> _beforeShield = new();

        /// <summary>The one-handed weapon the pet has out, None when it holds none.</summary>
        public static W Holding(GameSummonedPet pet)
        {
            if (pet == null || pet.ActiveWeaponSlot != eActiveWeaponSlot.Standard)
                return W.None;
            if (pet.KitWeapon != W.None)
                return pet.KitWeapon;
            return WeaponOfModel(pet.Inventory?.GetItem(eInventorySlot.RightHandWeapon)?.Model ?? 0);
        }

        /// <summary>At summon: a kit pet holds a one-handed weapon of its kit, and no pet keeps a shield.</summary>
        public static void OnSummoned(GameSummonedPet pet, ICharacterClass summonerClass)
        {
            if (pet == null || pet is CommanderPet || pet is BdSubPet || summonerClass == null)
                return;
            try
            {
                pet.Taunting = false;
                pet.TauntShielded = false;
                pet.KitWeapon = W.None;
                string servantSpec = pet is NecromancerPet ? ServantWeaponSpec(pet.Name) : null;
                if (servantSpec != null)
                {
                    ArmServant(pet, servantSpec);
                    return;
                }
                PetOrders.Kit kit = PetOrders.KitFor((eCharacterClass)summonerClass.ID, pet.Name);
                DbInventoryItem right = pet.Inventory?.GetItem(eInventorySlot.RightHandWeapon);
                if (kit.Weapons.Count > 0)
                {
                    W weapon = WeaponOfModel(right?.Model ?? 0);
                    if (!kit.Weapons.Contains(weapon))
                        weapon = kit.Weapons[Util.Random(0, kit.Weapons.Count - 1)];
                    Equip(pet, Model(weapon), 0, right?.Effect ?? 0);
                    pet.KitWeapon = weapon;
                    return;
                }
                // A pet that wields in its left hand (LeftHandSwingChance) fights with it; anything else there is a shield.
                if (pet.Inventory?.GetItem(eInventorySlot.LeftHandWeapon) != null && pet.LeftHandSwingChance <= 0)
                    Equip(pet, right?.Model ?? 0, 0, right?.Effect ?? 0, keepTwoHand: true);
            }
            catch (Exception e)
            {
                log.Warn($"PetKit.OnSummoned(): could not set up {pet.Name}'s weapons: {e.Message}");
            }
        }

        /// <summary>The owner's weapon pick. The shield stays while the pet taunts.</summary>
        public static bool Wield(GameNPC npc, W weapon)
        {
            if (npc is not GameSummonedPet pet || Model(weapon) == 0 || !pet.IsAlive)
                return false;
            DbInventoryItem right = pet.Inventory?.GetItem(eInventorySlot.RightHandWeapon);
            pet.KitWeapon = weapon;
            Equip(pet, Model(weapon), pet.TauntShielded ? ShieldModel(pet.Realm) : 0, right?.Effect ?? 0);
            SyncTauntShield(pet);
            return true;
        }

        /// <summary>Gives or takes the taunt shield (and its styles) so it matches the pet's taunt and weapon.</summary>
        public static void SyncTauntShield(GameSummonedPet pet)
        {
            if (pet == null || pet is CommanderPet || KeepsShield(pet))
                return;
            bool want = pet.Taunting && Holding(pet) != W.None && pet.LeftHandSwingChance <= 0;
            if (want == pet.TauntShielded)
                return;
            DbInventoryItem right = pet.Inventory?.GetItem(eInventorySlot.RightHandWeapon);
            if (want)
            {
                var saved = new Saved { Styles = pet.Styles == null ? null : new List<Style>(pet.Styles), BlockChance = pet.BlockChance };
                _beforeShield.AddOrUpdate(pet, saved);
                Equip(pet, right?.Model ?? 0, ShieldModel(pet.Realm), right?.Effect ?? 0);
                if (pet.BlockChance < ShieldBlockChance)
                    pet.BlockChance = ShieldBlockChance;
                var styles = new List<Style>(pet.Styles ?? new List<Style>());
                var have = new HashSet<int>(styles.Select(style => style.ID));
                styles.AddRange(ShieldStyles(pet.Level, pet.Realm).Where(style => have.Add(style.ID)));
                pet.Styles = styles;
                pet.TauntShielded = true;
                return;
            }

            Equip(pet, right?.Model ?? 0, 0, right?.Effect ?? 0);
            if (_beforeShield.TryGetValue(pet, out Saved before))
            {
                pet.Styles = before.Styles ?? new List<Style>();
                pet.BlockChance = before.BlockChance;
                _beforeShield.Remove(pet);
            }
            pet.TauntShielded = false;
        }

        /// <summary>Rebuilds the pet's inventory with these hands, keeping its armor. A closed template is shared, so it is never edited.</summary>
        private static void Equip(GameNPC pet, int rightModel, int leftModel, int rightEffect, bool keepTwoHand = false)
        {
            IGameInventory old = pet.Inventory;
            var template = new GameNpcInventoryTemplate();
            if (old != null)
            {
                for (eInventorySlot slot = eInventorySlot.HeadArmor; slot <= eInventorySlot.ArmsArmor; slot++)
                {
                    DbInventoryItem item = old.GetItem(slot);
                    if (item != null)
                        template.AddNPCEquipment(slot, item.Model, item.Color, item.Effect, item.Extension, item.Emblem);
                }
                if (keepTwoHand)
                {
                    foreach (eInventorySlot slot in new[] { eInventorySlot.TwoHandWeapon, eInventorySlot.DistanceWeapon })
                    {
                        DbInventoryItem item = old.GetItem(slot);
                        if (item != null)
                            template.AddNPCEquipment(slot, item.Model, item.Color, item.Effect, item.Extension, item.Emblem);
                    }
                }
            }
            if (rightModel > 0)
                template.AddNPCEquipment(eInventorySlot.RightHandWeapon, rightModel, 0, rightEffect);
            if (leftModel > 0)
                template.AddNPCEquipment(eInventorySlot.LeftHandWeapon, leftModel);

            eActiveWeaponSlot before = pet.ActiveWeaponSlot;
            pet.Inventory = new GameNPCInventory(template);
            if (keepTwoHand)
                pet.InitializeActiveWeaponFromInventory();
            else
                pet.SwitchWeapon(eActiveWeaponSlot.Standard);
            // SwitchWeapon only broadcasts a slot change; the models changed either way.
            if (pet.ObjectState == GameObject.eObjectState.Active && pet.ActiveWeaponSlot == before)
                pet.BroadcastLivingEquipmentUpdate();
        }

        private static readonly Logging.Logger log = Logging.LoggerManager.Create(typeof(PetKit));

        #endregion
    }
}
