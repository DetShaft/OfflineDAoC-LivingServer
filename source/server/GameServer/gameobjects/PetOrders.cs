using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// The clickable [keyword] order dialog for every player pet other than the Bonedancer commander (issue #35; the
    /// commander keeps its own, CommanderPet.DialogMenu). Right-click your pet (the new client sends /pet menu) or
    /// whisper it, and it answers with its orders: stance, the pet window's Assist mode, and movement where the pet
    /// moves at all. What it offers depends on the pet:
    /// <list type="bullet">
    /// <item>Pets that walk with you (Cabalist simulacra, Spiritmaster spirits, Enchanter underhill allies, Druid and
    /// Hunter animals, the Sluaghbinder's sluagh, charmed creatures): stance, assist, follow / stay / come.</item>
    /// <item>Animist turrets and Theurgist elementals do not follow their owner (ControlledMobBrain.FollowOwner), so
    /// they offer stance and assist only.</item>
    /// <item>Necromancer pets take [taunt] only: their weapons come with the summon (owner, 2026-11-17).</item>
    /// </list>
    /// Per-class kits (owner, 2026-11-17, step 2): Spiritmaster pets choose a one-handed sword, axe or hammer, and the
    /// Enchanter's Underhill Ally and Compatriot a one-handed blade, mace or dagger. Spiritmaster, Cabalist,
    /// Necromancer and the Enchanter's melee pets (Friend, Ally, Compatriot) can [taunt]. No pet spawns with a shield:
    /// taunting gives a pet that holds a one-handed weapon a shield and the shield styles of its level (PetKit).
    /// Orders go through the same GamePlayer.CommandNpc* calls as the pet window, so both stay in step.
    /// Real players' pets only; bot pets are driven by their bot's AI.
    /// </summary>
    public static class PetOrders
    {
        public enum Kind
        {
            None,
            Walker,
            Stationary,
            /// <summary>Taunt only: no stance, assist or movement lines (the necromancer's pet).</summary>
            TauntOnly,
        }

        /// <summary>A one-handed weapon a pet can be told to take up.</summary>
        public enum Weapon
        {
            None,
            Sword,
            Axe,
            Hammer,
            Blade,
            Mace,
            Dagger,
        }

        /// <summary>What a pet offers beyond its stance and movement: weapons it can take up, and taunt.</summary>
        public sealed class Kit
        {
            public static readonly Kit Plain = new(Array.Empty<Weapon>(), false);

            public Kit(IReadOnlyList<Weapon> weapons, bool taunts)
            {
                Weapons = weapons ?? Array.Empty<Weapon>();
                Taunts = taunts;
            }

            public IReadOnlyList<Weapon> Weapons { get; }
            public bool Taunts { get; }
        }

        /// <summary>What the pet is doing now, for the dialog.</summary>
        public readonly record struct KitState(Weapon Holding, bool Taunting, bool Shielded);

        public enum Order
        {
            None,
            Menu,
            Aggressive,
            Defensive,
            Passive,
            Assist,
            StopAssist,
            Follow,
            Stay,
            ComeHere,
            Taunt,
            StopTaunting,
        }

        /// <summary>The kind of pet a class keeps. None: no dialog (the commander has its own).</summary>
        public static Kind KindFor(eCharacterClass ownerClass) => ownerClass switch
        {
            eCharacterClass.Bonedancer => Kind.None,
            eCharacterClass.Necromancer => Kind.TauntOnly,
            eCharacterClass.Animist or eCharacterClass.Theurgist => Kind.Stationary,
            _ => Kind.Walker,
        };

        private static readonly Weapon[] MidgardWeapons = { Weapon.Sword, Weapon.Axe, Weapon.Hammer };
        private static readonly Weapon[] HiberniaWeapons = { Weapon.Blade, Weapon.Mace, Weapon.Dagger };

        /// <summary>
        /// The kit of a pet, from its owner's class and the pet's name (the Enchanter's five pets share one class).
        /// </summary>
        public static Kit KitFor(eCharacterClass ownerClass, string petName)
        {
            string name = (petName ?? string.Empty).ToLowerInvariant();
            switch (ownerClass)
            {
                case eCharacterClass.Spiritmaster:
                    return new Kit(MidgardWeapons, true);
                case eCharacterClass.Cabalist:
                case eCharacterClass.Necromancer:
                    return new Kit(null, true);
                case eCharacterClass.Enchanter:
                    if (name.Contains("ally") || name.Contains("compatriot"))
                        return new Kit(HiberniaWeapons, true);
                    // The Friend fights unarmed; the Companion and the Zealot are casters.
                    return name.Contains("friend") ? new Kit(null, true) : Kit.Plain;
                default:
                    return Kit.Plain;
            }
        }

        /// <summary>Movement orders only for a pet that walks with its owner; taunt only for a pet that taunts.</summary>
        public static bool Allows(Kind kind, Order order) => Allows(kind, Kit.Plain, order);

        public static bool Allows(Kind kind, Kit kit, Order order)
        {
            kit ??= Kit.Plain;
            if (order is Order.Taunt or Order.StopTaunting)
                return kind != Kind.None && kit.Taunts;
            return kind switch
            {
                Kind.None => false,
                Kind.TauntOnly => false,
                Kind.Stationary => order is not (Order.None or Order.Follow or Order.Stay or Order.ComeHere),
                _ => order != Order.None,
            };
        }

        /// <summary>A weapon keyword ("sword", "blunt", ...). None when the text names no weapon.</summary>
        public static Weapon ParseWeapon(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Weapon.None;
            return text.Trim().ToLowerInvariant() switch
            {
                "sword" => Weapon.Sword,
                "axe" => Weapon.Axe,
                "hammer" => Weapon.Hammer,
                "blade" => Weapon.Blade,
                "mace" or "blunt" or "club" => Weapon.Mace,
                "dagger" or "piercing" or "pierce" or "rapier" => Weapon.Dagger,
                _ => Weapon.None,
            };
        }

        public static string WeaponWord(Weapon weapon) => weapon switch
        {
            Weapon.None => string.Empty,
            _ => weapon.ToString().ToLowerInvariant(),
        };

        public static Order Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Order.Menu;
            return text.Trim().ToLowerInvariant() switch
            {
                "pet" or "menu" or "orders" or "commands" => Order.Menu,
                "aggressive" or "aggressively" or "aggro" => Order.Aggressive,
                "defensive" or "defend" => Order.Defensive,
                "passive" => Order.Passive,
                "assist" or "assist me" => Order.Assist,
                "stop assisting" or "stop" or "unassist" => Order.StopAssist,
                "follow" or "follow me" or "heel" => Order.Follow,
                "stay" or "stay here" or "hold" => Order.Stay,
                "come" or "come here" or "here" => Order.ComeHere,
                "taunt" => Order.Taunt,
                "stop taunting" or "stop taunt" or "untaunt" => Order.StopTaunting,
                _ => Order.None,
            };
        }

        /// <summary>The first line when you open the dialog, in the voice of the class's pet.</summary>
        public static string Greeting(eCharacterClass ownerClass, bool charmed)
        {
            if (charmed)
                return "(It watches you, bound to your will.)";
            return ownerClass switch
            {
                eCharacterClass.Necromancer => "(The servant of Arawn turns its dead eyes on you.)",
                eCharacterClass.Cabalist => "(The simulacrum turns to you, awaiting your will.)",
                eCharacterClass.Spiritmaster => "What would you have of me, master?",
                eCharacterClass.Hunter => "(Your companion looks up at you, ears pricked.)",
                eCharacterClass.Druid => "(The spirit of the wild stirs, awaiting your call.)",
                eCharacterClass.Enchanter => "(Your ally from the underhill bows its head.)",
                eCharacterClass.Animist => "(The growth leans toward you, waiting.)",
                eCharacterClass.Theurgist => "(The elemental churns, awaiting your command.)",
                eCharacterClass.Sluaghbinder => "(The sluagh whispers, waiting on your word.)",
                _ => "(Your pet awaits your command.)",
            };
        }

        /// <summary>The dialog: the answer (or greeting) on top, then a line for each order this pet takes.</summary>
        public static string Menu(Kind kind, string top, eAggressionState stance, bool assisting, eWalkState walk,
            Kit kit = null, KitState state = default)
        {
            kit ??= Kit.Plain;
            var lines = new List<string> { top };
            if (kind == Kind.TauntOnly)
            {
                AddKitLines(lines, kit, state);
                return string.Join("\n", lines);
            }
            if (assisting)
                lines.Add("I attack whatever you attack. Shall I [stop assisting]?");
            else
            {
                string now = stance switch
                {
                    eAggressionState.Aggressive => "I attack any foe that comes near.",
                    eAggressionState.Passive => "I do not fight, even when struck.",
                    _ => "I fight back when you or I are attacked.",
                };
                lines.Add(now + " Shall I [assist] you, attacking whatever you attack?");
            }
            var stances = new List<string>();
            if (assisting || stance != eAggressionState.Aggressive) stances.Add("[aggressive]");
            if (assisting || stance != eAggressionState.Defensive) stances.Add("[defensive]");
            if (stance != eAggressionState.Passive) stances.Add("[passive]");
            lines.Add("Or I can be " + Join(stances) + ".");

            if (kind == Kind.Walker)
            {
                lines.Add(walk switch
                {
                    eWalkState.Stay => "I hold this spot. Shall I [follow] you, or [come] to you?",
                    eWalkState.ComeHere or eWalkState.GoTarget => "I go where you sent me. Shall I [follow] you, or [stay]?",
                    _ => "I follow you. Shall I [stay] here?",
                });
            }
            AddKitLines(lines, kit, state);
            return string.Join("\n", lines);
        }

        private static void AddKitLines(List<string> lines, Kit kit, KitState state)
        {
            if (kit.Weapons.Count > 0)
            {
                var offered = new List<string>();
                foreach (Weapon weapon in kit.Weapons)
                {
                    if (weapon != state.Holding)
                        offered.Add("[" + WeaponWord(weapon) + "]");
                }
                string held = state.Holding == Weapon.None
                    ? "I hold no weapon."
                    : "I hold " + Article(WeaponWord(state.Holding)) + " " + WeaponWord(state.Holding) + ".";
                lines.Add(held + " I can take up " + Join(offered) + " instead.");
            }
            if (!kit.Taunts)
                return;
            if (state.Taunting)
            {
                lines.Add((state.Shielded
                    ? "I hold the enemy's eye behind my shield."
                    : "I am holding the enemy's eye.") + " Shall I [stop taunting]?");
            }
            else
                lines.Add("Shall I [taunt] the enemy away from you?");
        }

        private static string Article(string word) =>
            word.Length > 0 && "aeiou".IndexOf(char.ToLowerInvariant(word[0])) >= 0 ? "an" : "a";

        private static string Join(IReadOnlyList<string> items) => items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => items[0] + " or " + items[1],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " or " + items[^1],
        };

        #region Live

        /// <summary>The player's own main pet, when it takes this dialog.</summary>
        public static bool IsOrderable(GameNPC pet, GamePlayer player) =>
            pet != null && player?.CharacterClass != null && pet is not CommanderPet &&
            ReferenceEquals(player.ControlledBrain?.Body, pet) && pet.Brain is ControlledMobBrain &&
            KindFor((eCharacterClass)player.CharacterClass.ID) != Kind.None;

        public static Kit KitOf(GamePlayer player, GameNPC pet) =>
            player?.CharacterClass == null ? Kit.Plain : KitFor((eCharacterClass)player.CharacterClass.ID, pet?.Name);

        /// <summary>
        /// A keyword from the pet's owner: an order, a weapon or the menu. Null when the text is none of them (the
        /// caller leaves it to the normal path), otherwise the pet's answer (empty for the menu).
        /// </summary>
        public static string Handle(GamePlayer player, GameNPC pet, string text)
        {
            Kind kind = KindFor((eCharacterClass)player.CharacterClass.ID);
            Kit kit = KitOf(player, pet);
            Weapon weapon = ParseWeapon(text);
            if (weapon != Weapon.None)
            {
                if (!kit.Weapons.Contains(weapon))
                    return null;
                return PetKit.Wield(pet, weapon)
                    ? "I take up the " + WeaponWord(weapon) + "."
                    : "I cannot take up a " + WeaponWord(weapon) + " right now.";
            }
            Order order = Parse(text);
            if (order == Order.Menu)
                return string.Empty;
            if (order == Order.None || !Allows(kind, kit, order))
                return null;
            return Apply(player, pet, order);
        }

        /// <summary>A whisper to the pet from its owner: an order or the menu. False leaves the whisper to the normal path.</summary>
        public static bool TryWhisper(GameNPC pet, GameLiving source, string text)
        {
            if (source is not GamePlayer player || !IsOrderable(pet, player))
                return false;
            string reply = Handle(player, pet, text);
            if (reply == null)
                return false;
            Show(player, pet, reply.Length == 0 ? null : reply);
            return true;
        }

        /// <summary>Right-click (Interact) on your own pet opens its dialog.</summary>
        public static bool TryInteract(GameNPC pet, GamePlayer player)
        {
            if (!IsOrderable(pet, player))
                return false;
            Show(player, pet, null);
            return true;
        }

        /// <summary>Carries out an order and returns what the pet answers.</summary>
        public static string Apply(GamePlayer player, GameNPC pet, Order order)
        {
            switch (order)
            {
                case Order.Aggressive:
                    player.CommandNpcAgressive();
                    return "I will attack any foe that comes near.";
                case Order.Defensive:
                    player.CommandNpcDefensive();
                    return "I will fight back when we are attacked.";
                case Order.Passive:
                    player.CommandNpcPassive();
                    return "I will not fight.";
                case Order.Assist:
                    PetAssistMode.Set(player, true);
                    return "I will attack whatever you attack.";
                case Order.StopAssist:
                    PetAssistMode.Set(player, false);
                    return "I will only fight back when we are attacked.";
                case Order.Follow:
                    player.CommandNpcFollow();
                    return "I will follow you.";
                case Order.Stay:
                    player.CommandNpcStay();
                    return "I will hold here.";
                case Order.ComeHere:
                    player.CommandNpcComeHere();
                    return "I am coming.";
                case Order.Taunt:
                    if (pet is not GameSummonedPet tauntingPet)
                        return null;
                    tauntingPet.SetTaunting(true);
                    return tauntingPet.TauntShielded
                        ? "I raise my shield and draw the enemy to me."
                        : "I will draw the enemy to me.";
                case Order.StopTaunting:
                    if (pet is not GameSummonedPet quietPet)
                        return null;
                    quietPet.SetTaunting(false);
                    return "I will no longer taunt.";
                default:
                    return null;
            }
        }

        public static void Show(GamePlayer player, GameNPC pet, string reply)
        {
            if (player?.Out == null || pet?.Brain is not ControlledMobBrain brain)
                return;
            eCharacterClass ownerClass = (eCharacterClass)player.CharacterClass.ID;
            string top = string.IsNullOrWhiteSpace(reply) ? Greeting(ownerClass, pet is not GameSummonedPet) : reply;
            var state = pet is GameSummonedPet summoned
                ? new KitState(PetKit.Holding(summoned), summoned.Taunting, summoned.TauntShielded)
                : default;
            string text = Menu(KindFor(ownerClass), top, brain.AggressionState, PetAssistMode.IsOn(player), brain.WalkState,
                KitOf(player, pet), state);
            player.Out.SendMessage(text, eChatType.CT_Say, eChatLoc.CL_PopupWindow);
        }

        #endregion
    }
}
