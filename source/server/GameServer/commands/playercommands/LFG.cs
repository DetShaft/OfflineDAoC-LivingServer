using System.Collections.Generic;
using DOL.GS.PacketHandler;
using DOL.GS.Scripts.discord;
using DOL.GS.ServerProperties;
using DOL.Language;

namespace DOL.GS.Commands
{
    [CmdAttribute(
         "&lfg",
         ePrivLevel.Player,
         "Looking for group: broadcast, list players and advertising bots, or toggle your LFG flag",
         "/lfg <message>",
         "/lfg list [class] [min[-max]] [zone…]",
         "/lfg on",
         "/lfg off",
         "/lfg window")]
    public class LFGCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        private const string LFG_TIMEOUT_KEY = "lastLFGTick";

        public void OnCommand(GameClient client, string[] args)
        {
            if (args.Length < 2)
            {
                DisplayMessage(client, "Usage: /lfg <message> | /lfg list [class] [level|min-max] [zone] | /lfg on|off|window");
                return;
            }

            string verb = args[1].ToLowerInvariant();
            if (verb == "list")
            {
                LfgListFilters filters = LfgListFilters.Parse(args, 2);
                IList<LfgWindowEntry> entries = LfgBrowser.BuildFor(client.Player, filters);
                foreach (string line in LfgBrowser.FormatTextLines(entries))
                    DisplayMessage(client, line);
                return;
            }

            if (verb == "on")
            {
                GroupMgr.SetPlayerLooking(client.Player);
                DisplayMessage(client, "You are now flagged as looking for a group.");
                return;
            }

            if (verb == "off")
            {
                GroupMgr.RemovePlayerLooking(client.Player);
                DisplayMessage(client, "You are no longer flagged as looking for a group.");
                return;
            }

            if (verb == "window")
            {
                // Refresh the classic / new-client find-group window with the same mixed list.
                IList<LfgWindowEntry> entries = LfgBrowser.BuildFor(client.Player);
                var array = new LfgWindowEntry[entries.Count];
                for (int i = 0; i < entries.Count; i++)
                    array[i] = entries[i];
                client.Out.SendFindGroupWindowUpdate(array);
                return;
            }

            if (client.Player.IsMuted)
            {
                client.Player.Out.SendMessage("You have been muted. You cannot broadcast.", eChatType.CT_Staff, eChatLoc.CL_SystemWindow);
                return;
            }

            string message = string.Join(" ", args, 1, args.Length - 1);
            long lastLfgTick = client.Player.TempProperties.GetProperty<long>(LFG_TIMEOUT_KEY);
            int slowModeLength = Properties.LFG_SLOWMODE_LENGTH * 1000;

            if ((GameLoop.GameLoopTime - lastLfgTick) < slowModeLength && client.Account.PrivLevel == 1) // 60 secs
            {
                // Message: You must wait {0} seconds before using this command again.
                ChatUtil.SendSystemMessage(client, "PLCommands.LFG.List.Wait", Properties.LFG_SLOWMODE_LENGTH - (GameLoop.GameLoopTime - lastLfgTick) / 1000);
                return;
            }

            Broadcast(client.Player, message);
        }

        private static void Broadcast(GamePlayer player, string message)
        {
            foreach (GamePlayer otherPlayer in ClientService.Instance.GetPlayersForRealmWideChatMessage(player))
                otherPlayer.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Scripts.Players.LFG.Message", $"{player.Name} ({player.Level}, {player.CharacterClass.Name})", message), eChatType.CT_LFG, eChatLoc.CL_ChatWindow);

            if (Properties.DISCORD_ACTIVE)
                WebhookMessage.LogChatMessage(player, eChatType.CT_LFG, message);

            if (player.Client.Account.PrivLevel == 1)
                player.Client.Player.TempProperties.SetProperty(LFG_TIMEOUT_KEY, GameLoop.GameLoopTime);

            // Offline DAoC 1.1.3: the player's post is in the chat transcript (CHAT_LINE who=player ch=lfg).
            BotChatTranscript.Player("lfg", player, message);
            // Offline DAoC (the talking update): a solo gamebot of about your level may answer by tell.
            AutonomousLivingServer.OnPlayerLfg(player, message);
        }
    }
}
