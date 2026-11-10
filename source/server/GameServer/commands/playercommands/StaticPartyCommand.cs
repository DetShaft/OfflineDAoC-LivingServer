using System;
using System.Linq;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    [CmdAttribute(
        "&static",
        ePrivLevel.Player,
        "Manage your saved static party of gamebots (auto-invite on login when enabled).",
        "/static add <name>",
        "/static remove <name>",
        "/static list",
        "/static invite",
        "/static auto on|off")]
    public class StaticPartyCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null)
                return;

            if (args.Length < 2)
            {
                ShowHelp(client);
                return;
            }

            string verb = args[1].Trim().ToLowerInvariant();
            switch (verb)
            {
                case "add":
                {
                    string name = string.Join(" ", args.Skip(2));
                    PlayerStaticParty.TryAdd(client.Player, name, out string message);
                    DisplayMessage(client, message);
                    return;
                }
                case "remove":
                case "rem":
                case "del":
                case "delete":
                {
                    string name = string.Join(" ", args.Skip(2));
                    PlayerStaticParty.TryRemove(client.Player, name, out string message);
                    DisplayMessage(client, message);
                    return;
                }
                case "list":
                {
                    string[] party = PlayerStaticParty.List(client.Player);
                    bool auto = PlayerStaticParty.AutoInviteEnabled(client.Player);
                    if (party.Length == 0)
                        DisplayMessage(client, $"Static party empty ({PlayerStaticParty.MaxSlots} slots). Auto-invite: {(auto ? "on" : "off")}.");
                    else
                        DisplayMessage(client, $"Static party ({party.Length}/{PlayerStaticParty.MaxSlots}, auto {(auto ? "on" : "off")}): {string.Join(", ", party)}");
                    return;
                }
                case "invite":
                case "form":
                case "call":
                {
                    if (IsSpammingCommand(client.Player, "static_invite"))
                        return;
                    PlayerStaticParty.InviteAll(client.Player, announceMissing: true);
                    return;
                }
                case "auto":
                {
                    if (args.Length < 3)
                    {
                        DisplayMessage(client, $"Auto-invite on login is {(PlayerStaticParty.AutoInviteEnabled(client.Player) ? "on" : "off")}.");
                        return;
                    }
                    string flag = args[2].Trim().ToLowerInvariant();
                    if (flag is "on" or "1" or "true" or "yes")
                    {
                        PlayerStaticParty.SetAutoInvite(client.Player, true);
                        DisplayMessage(client, "Static party will auto-invite on login when members are online and nearby.");
                    }
                    else if (flag is "off" or "0" or "false" or "no")
                    {
                        PlayerStaticParty.SetAutoInvite(client.Player, false);
                        DisplayMessage(client, "Static party auto-invite on login is off. Use /static invite when you want them.");
                    }
                    else
                        DisplayMessage(client, "Usage: /static auto on|off");
                    return;
                }
                case "help":
                default:
                    ShowHelp(client);
                    return;
            }
        }

        private void ShowHelp(GameClient client)
        {
            DisplayMessage(client, "Static party: /static add|remove <name>, /static list, /static invite, /static auto on|off");
            DisplayMessage(client, $"Save up to {PlayerStaticParty.MaxSlots} favorite gamebots; invite when they are online and nearby.");
        }
    }
}
