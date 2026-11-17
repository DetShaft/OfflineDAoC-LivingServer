using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /look lists the drops beside you and /look take puts the one you choose in your backpack.
    /// In a group it takes a share that waits on the ground for you: a turn dealt with /autoloot off,
    /// or a roll you won with a full backpack. It does not take a bot's share. Gear is rolled for with /loot.
    /// </summary>
    [CmdAttribute(
        "&look",
        ePrivLevel.Player,
        "Look through the drops beside you and choose what to take",
        "/look - list the drops you can take",
        "/look take <number or name> - put that drop in your backpack")]
    public class LookCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null)
                return;

            ushort radius = (ushort)Math.Clamp(Properties.WORLD_PICKUP_DISTANCE, 1, ushort.MaxValue);
            List<GameStaticItemTimed> drops = GroupLoot.LookList(player, player.GetItemsInRadius(radius));
            if (args == null || args.Length < 2 || !args[1].Equals("take", StringComparison.OrdinalIgnoreCase))
            {
                Show(client, drops);
                return;
            }

            string request = args.Length > 2 ? string.Join(" ", args.Skip(2)) : string.Empty;
            if (!GroupLoot.TryChoose(drops, request, out GameStaticItemTimed chosen, out string error))
            {
                client.Out.SendMessage(error, eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            player.PickupObject(chosen, false);
        }

        private static void Show(GameClient client, IReadOnlyList<GameStaticItemTimed> drops)
        {
            if (drops.Count == 0)
            {
                client.Out.SendMessage("Nothing here is yours to look through. Stand next to the corpse and /look again.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            client.Out.SendMessage("You look over the drops:", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            for (int index = 0; index < drops.Count; index++)
                client.Out.SendMessage($"  {index + 1}. {GroupLoot.Label(drops[index])}", eChatType.CT_Loot, eChatLoc.CL_SystemWindow);
            client.Out.SendMessage("Use /look take <number> to put one in your backpack.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
