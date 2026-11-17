using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Orders for your pet (issue #35), for every pet class except the Bonedancer commander (/commander) and the
    /// necromancer's shade pet. "/pet" or "/pet menu" opens the pet's [keyword] dialog (the new client sends this when
    /// you right-click your pet); "/pet &lt;order&gt;" gives one order directly. See PetOrders.
    /// </summary>
    [CmdAttribute(
        "&pet",
        ePrivLevel.Player,
        "Give orders to your pet, or open its order dialog",
        "/pet",
        "/pet menu",
        "/pet <aggressive|defensive|passive|assist|stop assisting|follow|stay|come>")]
    public class PetCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null || IsSpammingCommand(player, "pet", 250))
                return;

            GameNPC pet = player.ControlledBrain?.Body;
            if (pet is CommanderPet)
            {
                // The commander keeps its own dialog; send the player there rather than refuse.
                if (args.Length < 2 || PetOrders.Parse(args[1]) == PetOrders.Order.Menu)
                    ((CommanderPet)pet).ShowDialog(player, append: false);
                else
                    Say(client, "Your commander takes its orders through /commander.");
                return;
            }
            if (!PetOrders.IsOrderable(pet, player))
            {
                Say(client, pet == null ? "You don't have a pet." : "Your pet can't take orders this way.");
                return;
            }

            string text = args.Length < 2 ? string.Empty : string.Join(" ", args, 1, args.Length - 1);
            PetOrders.Order order = PetOrders.Parse(text);
            PetOrders.Kind kind = PetOrders.KindFor((eCharacterClass)player.CharacterClass.ID);
            if (order == PetOrders.Order.Menu)
            {
                PetOrders.Show(player, pet, null);
                return;
            }
            if (order == PetOrders.Order.None)
            {
                Say(client, "Orders: aggressive, defensive, passive, assist, stop assisting, follow, stay, come.");
                return;
            }
            if (!PetOrders.Allows(kind, order))
            {
                Say(client, $"{pet.GetName(0, true)} does not move from where it was placed.");
                return;
            }
            PetOrders.Show(player, pet, PetOrders.Apply(player, pet, order));
        }

        private static void Say(GameClient client, string text) =>
            client.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }
}
