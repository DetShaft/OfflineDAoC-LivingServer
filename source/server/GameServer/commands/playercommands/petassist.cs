using DOL.AI.Brain;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// The pet window's ASSIST mode (PetAssistMode): your pet attacks your target as soon as you do.
    /// The new client's pet window ASSIST button sends "/petassist on"; on the classic client, type it.
    /// Picking Aggressive, Defensive or Passive on the pet window turns it off again.
    /// </summary>
    [CmdAttribute(
        "&petassist",
        ePrivLevel.Player,
        "Your pet attacks your target as soon as you engage it",
        "/petassist [on|off]")]
    public class PetAssistCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null)
                return;

            bool next = !PetAssistMode.IsOn(player);
            if (args.Length >= 2)
            {
                string value = args[1].ToLowerInvariant();
                if (value is "on" or "1" or "true" or "yes")
                    next = true;
                else if (value is "off" or "0" or "false" or "no")
                    next = false;
                else
                {
                    Say(client, "Use /petassist on or /petassist off.");
                    return;
                }
            }

            PetAssistMode.Set(player, next);
            GameNPC pet = player.ControlledBrain?.Body;
            string petName = pet?.GetName(0, true) ?? "Your pet";

            if (!next)
            {
                Say(client, pet != null
                    ? $"{petName} stops assisting you and stays defensive."
                    : "Assist is off. Your pets will be defensive.");
                return;
            }

            if (pet == null)
                Say(client, "Assist is on: the next pet you summon attacks your target as soon as you do.");
            else if (player.ControlledBrain is ControlledMobBrain brain && PetAssistMode.Assists(brain))
                Say(client, $"{petName} will assist you: it attacks your target as soon as you do.");
            else
                Say(client, $"Assist is on, but {pet.GetName(0, false)} can't be ordered to assist.");
        }

        private static void Say(GameClient client, string text)
        {
            client.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
