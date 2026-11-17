using System;
using System.Collections.Generic;
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
    /// <item>Necromancer shades are left alone: the necromancer's pet has its own control path.</item>
    /// </list>
    /// Orders go through the same GamePlayer.CommandNpc* calls as the pet window, so both stay in step. Weapon kits and
    /// styles are not offered yet: those need a per-class decision on what each pet historically used.
    /// Real players' pets only; bot pets are driven by their bot's AI.
    /// </summary>
    public static class PetOrders
    {
        public enum Kind
        {
            None,
            Walker,
            Stationary,
        }

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
        }

        /// <summary>The kind of pet a class keeps. None: no dialog (the commander and the necromancer have their own).</summary>
        public static Kind KindFor(eCharacterClass ownerClass) => ownerClass switch
        {
            eCharacterClass.Bonedancer or eCharacterClass.Necromancer => Kind.None,
            eCharacterClass.Animist or eCharacterClass.Theurgist => Kind.Stationary,
            _ => Kind.Walker,
        };

        /// <summary>Movement orders only for a pet that walks with its owner.</summary>
        public static bool Allows(Kind kind, Order order) => kind switch
        {
            Kind.None => false,
            Kind.Stationary => order is not (Order.Follow or Order.Stay or Order.ComeHere),
            _ => order != Order.None,
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
        public static string Menu(Kind kind, string top, eAggressionState stance, bool assisting, eWalkState walk)
        {
            var lines = new List<string> { top };
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
            return string.Join("\n", lines);
        }

        private static string Join(IReadOnlyList<string> items) => items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => items[0] + " or " + items[1],
            _ => string.Join(", ", System.Linq.Enumerable.Take(items, items.Count - 1)) + " or " + items[^1],
        };

        #region Live

        /// <summary>The player's own main pet, when it takes this dialog.</summary>
        public static bool IsOrderable(GameNPC pet, GamePlayer player) =>
            pet != null && player?.CharacterClass != null && pet is not CommanderPet &&
            ReferenceEquals(player.ControlledBrain?.Body, pet) && pet.Brain is ControlledMobBrain &&
            KindFor((eCharacterClass)player.CharacterClass.ID) != Kind.None;

        /// <summary>A whisper to the pet from its owner: an order or the menu. False leaves the whisper to the normal path.</summary>
        public static bool TryWhisper(GameNPC pet, GameLiving source, string text)
        {
            if (source is not GamePlayer player || !IsOrderable(pet, player))
                return false;
            Order order = Parse(text);
            Kind kind = KindFor((eCharacterClass)player.CharacterClass.ID);
            if (order == Order.None || order != Order.Menu && !Allows(kind, order))
                return false;
            Show(player, pet, order == Order.Menu ? null : Apply(player, pet, order));
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
            string text = Menu(KindFor(ownerClass), top, brain.AggressionState, PetAssistMode.IsOn(player), brain.WalkState);
            player.Out.SendMessage(text, eChatType.CT_Say, eChatLoc.CL_PopupWindow);
        }

        #endregion
    }
}
