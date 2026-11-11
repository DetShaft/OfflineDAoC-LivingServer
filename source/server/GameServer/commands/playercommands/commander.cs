using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;
using DOL.GS.Styles;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Bonedancer commander controls: weapon, style preference, taunt, assist.
    /// The new client opens the commander's clickable [keyword] dialog (interact or /commander menu).
    /// This command is the same orders for a typed line.
    /// </summary>
    [CmdAttribute(
        "&commander",
        ePrivLevel.Player,
        "Control your Bonedancer commander's weapon, styles and taunt",
        "/commander",
        "/commander menu",
        "/commander weapon <1h-axe|1h-hammer|1h-sword|2h-axe|2h-hammer|2h-sword|bow|axe|hammer|sword>",
        "/commander style [name|auto|list]",
        "/commander taunt [on|off]",
        "/commander assist [on|off]",
        "/commander spell <damage|debuff|other|drain|suppress|snares|debilitating>")]
    public class CommanderCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            // Weapon clicks used to share the 1500 ms silent drop with every other /commander
            // line. A second click inside that window did nothing, on top of a same-slot swap
            // that never told the client the model changed.
            bool weaponOrMenu = args.Length >= 2 && args[1].ToLowerInvariant() is "weapon" or "menu";
            if (weaponOrMenu)
            {
                if (IsSpammingCommand(client.Player, "commander-" + args[1].ToLowerInvariant(), 250))
                {
                    client.Out.SendMessage("Give your commander a moment.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }
            }
            else if (IsSpammingCommand(client.Player, "commander"))
                return;

            GamePlayer player = client.Player;
            if (player.CharacterClass.ID != (int)eCharacterClass.Bonedancer)
            {
                client.Out.SendMessage("Only Bonedancers can use this command!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            CommanderPet commander = player.ControlledBrain?.Body as CommanderPet;
            if (commander == null)
            {
                client.Out.SendMessage("You don't have a commander!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (args.Length < 2)
            {
                ShowStatus(client, commander);
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "weapon":
                    HandleWeapon(client, commander, args);
                    break;
                case "style":
                    HandleStyle(client, commander, args);
                    break;
                case "taunt":
                    HandleToggle(client, commander, args, taunt: true);
                    break;
                case "assist":
                    HandleToggle(client, commander, args, taunt: false);
                    break;
                case "spell":
                    HandleSpell(client, commander, args);
                    break;
                case "menu":
                case "orders":
                    commander.ShowDialog(player, append: false);
                    break;
                case "status":
                case "info":
                    ShowStatus(client, commander);
                    break;
                default:
                    client.Out.SendMessage("Unrecognized argument: " + args[1], eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    ShowStatus(client, commander);
                    break;
            }
        }

        private static void ShowStatus(GameClient client, CommanderPet commander)
        {
            client.Out.SendMessage(
                $"Commander: {commander.Name} ({commander.CommanderType})  taunt={(commander.Taunting ? "on" : "off")}  assist={(commander.MinionsAssisting ? "on" : "off")}",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);

            IList<CommanderPet.eWeaponType> weapons = commander.AvailableWeaponTypes();
            if (weapons.Count > 0)
            {
                string list = string.Join(", ", weapons.Select(WeaponToken));
                client.Out.SendMessage($"Weapons: {list}", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
            else
                client.Out.SendMessage("Weapons: this commander cannot change weapons.", eChatType.CT_System, eChatLoc.CL_SystemWindow);

            string style = commander.PreferredCombatStyle?.Name ?? "auto";
            client.Out.SendMessage($"Style: {style}", eChatType.CT_System, eChatLoc.CL_SystemWindow);

            if (commander.CommanderType is CommanderPet.eCommanderType.DreadGuardian or CommanderPet.eCommanderType.DreadLich)
                client.Out.SendMessage($"Spell preference: {commander.PreferredSpell}", eChatType.CT_System, eChatLoc.CL_SystemWindow);

            client.Out.SendMessage(
                "Usage: /commander weapon <type> | style [name|auto|list] | taunt [on|off] | assist [on|off] | spell <mode>",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static void HandleWeapon(GameClient client, CommanderPet commander, string[] args)
        {
            if (args.Length < 3)
            {
                IList<CommanderPet.eWeaponType> weapons = commander.AvailableWeaponTypes();
                if (weapons.Count == 0)
                {
                    client.Out.SendMessage("This commander cannot change weapons.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }
                client.Out.SendMessage("Available weapons: " + string.Join(", ", weapons.Select(WeaponToken)),
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (!CommanderPet.TryParseWeaponType(args[2], out CommanderPet.eWeaponType weaponType))
            {
                client.Out.SendMessage("Unknown weapon. Try 1h-axe, 1h-hammer, 1h-sword, 2h-axe, 2h-hammer, 2h-sword, bow.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (!commander.AvailableWeaponTypes().Contains(weaponType))
            {
                client.Out.SendMessage("Your commander cannot use that weapon.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (!commander.CommanderSwitchWeapon(weaponType))
            {
                client.Out.SendMessage("Your commander could not change weapons.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            // CommanderSwitchWeapon drops a preferred style the new weapon cannot use.
            client.Out.SendMessage($"Commander weapon set to {WeaponToken(weaponType)}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static void HandleStyle(GameClient client, CommanderPet commander, string[] args)
        {
            List<Style> styles = CollectStyles(commander);
            if (args.Length < 3 || string.Equals(args[2], "list", StringComparison.OrdinalIgnoreCase))
            {
                if (styles.Count == 0)
                {
                    client.Out.SendMessage("Your commander has no styles for the weapon it is holding.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }
                client.Out.SendMessage("Commander styles (pick with /commander style <name>, or 'auto'):",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                foreach (Style style in styles)
                    client.Out.SendMessage($"  {style.Name}", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                string current = commander.PreferredCombatStyle?.Name ?? "auto";
                client.Out.SendMessage($"Current: {current}", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (string.Equals(args[2], "auto", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[2], "clear", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[2], "off", StringComparison.OrdinalIgnoreCase))
            {
                commander.SetPreferredCombatStyle(null);
                client.Out.SendMessage("Commander style set to auto.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            string wanted = string.Join(" ", args, 2, args.Length - 2);
            Style match = styles.FirstOrDefault(s =>
                string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                match = styles.FirstOrDefault(s =>
                    s.Name != null && s.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));
            }
            if (match == null)
            {
                client.Out.SendMessage($"No style named '{wanted}'. Use /commander style list.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            commander.SetPreferredCombatStyle(match);
            client.Out.SendMessage($"Commander will prefer style '{match.Name}'.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static void HandleToggle(GameClient client, CommanderPet commander, string[] args, bool taunt)
        {
            bool? forced = null;
            if (args.Length >= 3)
            {
                string v = args[2].ToLowerInvariant();
                if (v is "on" or "1" or "true" or "yes")
                    forced = true;
                else if (v is "off" or "0" or "false" or "no")
                    forced = false;
                else
                {
                    client.Out.SendMessage("Use on or off.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }
            }

            if (taunt)
            {
                bool next = forced ?? !commander.Taunting;
                commander.SetTaunting(next);
                client.Out.SendMessage(next ? "Your commander starts taunting." : "Your commander stops taunting.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (commander.ControlledNpcList == null || commander.ControlledNpcList.All(b => b == null))
            {
                client.Out.SendMessage("You don't have any minions!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            bool assist = forced ?? !commander.MinionsAssisting;
            commander.SetMinionsAssisting(assist);
            client.Out.SendMessage(assist ? "Minions will assist the commander." : "Minions will not assist the commander.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static void HandleSpell(GameClient client, CommanderPet commander, string[] args)
        {
            if (commander.CommanderType is not (CommanderPet.eCommanderType.DreadGuardian or CommanderPet.eCommanderType.DreadLich))
            {
                client.Out.SendMessage("Only Dread Guardians and Dread Liches have spell preferences.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (args.Length < 3)
            {
                client.Out.SendMessage($"Current spell preference: {commander.PreferredSpell}", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                client.Out.SendMessage("Usage: /commander spell <damage|debuff|other|drain|suppress|snares|debilitating>",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            string key = args[2].ToLowerInvariant();
            CommanderPet.eCommanderPreferredSpell preferred = key switch
            {
                "damage" or "suppress" => CommanderPet.eCommanderPreferredSpell.Damage,
                "debuff" or "debilitating" or "disease" => CommanderPet.eCommanderPreferredSpell.Debuff,
                "other" or "drain" or "snare" or "snares" or "lifedrain" => CommanderPet.eCommanderPreferredSpell.Other,
                "none" or "auto" => CommanderPet.eCommanderPreferredSpell.None,
                _ => (CommanderPet.eCommanderPreferredSpell)(-1),
            };
            if ((int)preferred < 0)
            {
                client.Out.SendMessage("Unknown spell mode.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            commander.SetPreferredSpell(preferred);
            client.Out.SendMessage($"Commander spell preference set to {preferred}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static List<Style> CollectStyles(CommanderPet commander) => commander.StylesForEquippedWeapon();

        private static string WeaponToken(CommanderPet.eWeaponType type) => type switch
        {
            CommanderPet.eWeaponType.OneHandAxe => "1h-axe",
            CommanderPet.eWeaponType.OneHandHammer => "1h-hammer",
            CommanderPet.eWeaponType.OneHandSword => "1h-sword",
            CommanderPet.eWeaponType.TwoHandAxe => "2h-axe",
            CommanderPet.eWeaponType.TwoHandHammer => "2h-hammer",
            CommanderPet.eWeaponType.TwoHandSword => "2h-sword",
            CommanderPet.eWeaponType.Bow => "bow",
            _ => type.ToString(),
        };
    }
}
