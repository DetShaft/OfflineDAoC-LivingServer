namespace DOL.GS.Commands
{
    /// <summary>
    /// /combatlog details on|off|default (issue #99): whether you see your /spawn helpers' damage numbers, crits, styles
    /// and spell lines in your others' combat log. Off by default, as on live for another player's fighting. The choice
    /// is saved on the account, so every character and both clients share it; "default" follows the server setting
    /// <c>bots / bot_combat_log_details</c> again. The new client's Options &gt; CHAT toggle sends this command.
    /// </summary>
    [CmdAttribute(
        "&combatlog",
        ePrivLevel.Player,
        "Show or hide your helpers' damage numbers in your combat log.",
        "/combatlog details - shows whether your helpers' damage numbers, crits, styles and spell lines are shown",
        "/combatlog details on|off - shows or hides them (saved for all your characters)",
        "/combatlog details default - follows the server setting again")]
    public class CombatLogCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;

            if (player == null)
                return;

            if (args.Length < 2 || !args[1].Equals("details", System.StringComparison.OrdinalIgnoreCase))
            {
                DisplaySyntax(client);
                return;
            }

            if (args.Length >= 3)
            {
                string choice = args[2].ToLowerInvariant() switch
                {
                    "on" or "true" or "1" => "on",
                    "off" or "false" or "0" => "off",
                    "default" or "reset" => string.Empty,
                    _ => null,
                };

                if (choice == null)
                {
                    DisplaySyntax(client);
                    return;
                }

                CombatLogAttribution.SaveDetailsChoice(player, choice);

                // The new client re-sends the saved Options choice on entering the world without a chat line.
                if (args.Length >= 4 && args[3].Equals("quiet", System.StringComparison.OrdinalIgnoreCase))
                    return;
            }

            DisplayMessage(client, StatusMessage(CombatLogAttribution.ReadDetailsChoice(player), CombatLogAttribution.ShowDetails));
        }

        /// <summary>The reply line for a saved choice and the server setting. Pure.</summary>
        public static string StatusMessage(string choice, bool serverDefault)
        {
            bool on = CombatLogAttribution.ResolveDetails(choice, serverDefault);
            string state = on
                ? "Your helpers' damage numbers, crits, styles and spell lines are shown in your others' combat log."
                : "Your helpers' fighting shows like another player's, without damage numbers.";
            return string.IsNullOrEmpty(choice) ? state + " (server setting)" : state;
        }
    }
}
