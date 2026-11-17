using System;
using System.Collections.Generic;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /loot answers a group's need or greed roll. The new client's loot window sends these
    /// from its buttons; the original client types them.
    /// </summary>
    [CmdAttribute(
        "&loot",
        ePrivLevel.Player,
        "Roll need or greed for gear your group is rolling for",
        "/loot - list the rolls waiting on you",
        "/loot need [number] - roll Need: you will use it",
        "/loot greed [number] - roll Greed: you would take it if nobody needs it",
        "/loot pass [number] - leave it to the others")]
    public class LootCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null)
                return;

            if (args == null || args.Length < 2)
            {
                Show(client, GroupLootRolls.WaitingOn(player));
                return;
            }

            eLootChoice choice = args[1].ToLowerInvariant() switch
            {
                "need" or "n" => eLootChoice.Need,
                "greed" or "g" => eLootChoice.Greed,
                "pass" or "p" => eLootChoice.Pass,
                _ => eLootChoice.Waiting
            };
            if (choice == eLootChoice.Waiting)
            {
                DisplaySyntax(client);
                return;
            }

            int id = 0;
            if (args.Length > 2 && (!int.TryParse(args[2], out id) || id <= 0))
            {
                client.Out.SendMessage("Give the roll's number, for example /loot need 3.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (!GroupLootRolls.Answer(player, id, choice, out string error))
                client.Out.SendMessage(error, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static void Show(GameClient client, IReadOnlyList<LootRoll> rolls)
        {
            if (rolls.Count == 0)
            {
                client.Out.SendMessage("No loot roll is waiting on you.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            foreach (LootRoll roll in rolls)
                client.Out.SendMessage(GroupLootRolls.StartLine(roll), eChatType.CT_Loot, eChatLoc.CL_SystemWindow);
            client.Out.SendMessage("Choose with /loot need, /loot greed or /loot pass and the roll's number.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
