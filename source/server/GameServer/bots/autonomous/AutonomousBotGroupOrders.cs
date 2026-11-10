using System;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Conservative /group orders from the player leader to grouped bots.
/// Small fixed vocabulary only — high misparse risk elsewhere, so free-form chat
/// still goes through the ordinary voice replies and does not move bots.
/// </summary>
public static class AutonomousBotGroupOrders
{
    public enum OrderKind
    {
        None,
        Attack,
        Assist,
        Buff,
        Follow,
        Stay
    }

    public static bool Enabled => Properties.BOT_GROUP_ORDERS;

    /// <summary>
    /// Pure parse of a short leader order. Rejects anything outside the fixed phrases.
    /// </summary>
    public static OrderKind Parse(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return OrderKind.None;

        string[] words = NormalizeWords(message);
        if (words.Length is < 1 or > 4)
            return OrderKind.None;

        // Strip a leading please / ok / k so "pls follow me" still matches.
        int start = words.Length > 1 && words[0] is "ok" or "k" or "pls" or "please" or "plz" ? 1 : 0;
        if (start >= words.Length)
            return OrderKind.None;

        string head = words[start];
        string[] rest = words.Skip(start + 1).ToArray();

        return head switch
        {
            "attack" when MatchesObject(rest) => OrderKind.Attack,
            "assist" when rest.Length == 0 || rest is ["me"] || rest is ["on", "me"] => OrderKind.Assist,
            "buff" when rest.Length == 0 || rest is ["us"] or ["me"] or ["up"] or ["please"] => OrderKind.Buff,
            "buffs" when rest.Length == 0 || rest is ["us"] or ["please"] => OrderKind.Buff,
            "follow" when rest.Length == 0 || rest is ["me"] || rest is ["us"] => OrderKind.Follow,
            "stay" when rest.Length == 0 || rest is ["here"] || rest is ["put"] || rest is ["still"] => OrderKind.Stay,
            "hold" when rest is ["position"] || rest is ["here"] => OrderKind.Stay,
            _ => OrderKind.None
        };
    }

    /// <summary>
    /// If this is a recognized leader order, execute it on grouped bots and return true.
    /// Acknowledgements are returned for the caller to send in /group.
    /// </summary>
    public static bool TryHandle(GamePlayer player, string message, out GameBot speaker, out string acknowledgement)
    {
        speaker = null;
        acknowledgement = null;
        if (!Enabled || player?.Group == null || string.IsNullOrWhiteSpace(message))
            return false;
        if (player.Group.Leader != player)
            return false;

        OrderKind order = Parse(message);
        if (order == OrderKind.None)
            return false;

        GameBot[] bots = player.Group.GetMembersInTheGroup().OfType<GameBot>()
            .Where(bot => bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active &&
                          bot.CurrentRegionID == player.CurrentRegionID)
            .ToArray();
        if (bots.Length == 0)
            return false;

        GameLiving attackTarget = ResolveAttackTarget(player);
        int acted = 0;
        foreach (GameBot bot in bots)
        {
            if (Execute(bot, player, order, attackTarget))
            {
                acted++;
                speaker ??= bot;
            }
        }

        if (acted == 0)
        {
            speaker = bots[0];
            acknowledgement = order is OrderKind.Attack or OrderKind.Assist
                ? "no target"
                : "cant";
            return true;
        }

        acknowledgement = Acknowledgement(order, acted);
        return true;
    }

    public static string Acknowledgement(OrderKind order, int acted = 1) => order switch
    {
        OrderKind.Attack or OrderKind.Assist => acted > 1 ? "on it" : "attacking",
        OrderKind.Buff => "buffing",
        OrderKind.Follow => "following",
        OrderKind.Stay => "staying",
        _ => "k"
    };

    public static void SendAcknowledgement(GameBot bot, string text)
    {
        if (bot?.Group == null || string.IsNullOrWhiteSpace(text))
            return;
        bot.Group.SendMessageToGroupMembers(bot, text, eChatType.CT_Group, eChatLoc.CL_ChatWindow);
        BotChatTranscript.Bot("group", bot.Name, bot.Realm, text);
    }

    private static bool Execute(GameBot bot, GamePlayer leader, OrderKind order, GameLiving attackTarget)
    {
        if (bot.Brain is not BotBrain brain)
            return false;

        switch (order)
        {
            case OrderKind.Attack:
            case OrderKind.Assist:
                if (attackTarget == null || !brain.CanAggroTarget(attackTarget))
                    return false;
                brain.OrderAssistCombat(attackTarget);
                return true;
            case OrderKind.Buff:
                brain.OrderBuffs();
                return true;
            case OrderKind.Follow:
                brain.OrderFollow(leader);
                return true;
            case OrderKind.Stay:
                brain.Stay();
                return true;
            default:
                return false;
        }
    }

    private static GameLiving ResolveAttackTarget(GamePlayer player)
    {
        GameLiving leaderTarget = PlayerLedPullCoordinator.FindLeaderTarget(player);
        if (leaderTarget != null)
            return leaderTarget;
        return player.TargetObject is GameLiving living && living.IsAlive ? living : null;
    }

    private static bool MatchesObject(string[] rest) =>
        rest.Length == 0 ||
        rest is ["it"] or ["him"] or ["her"] or ["them"] or ["that"] or ["this"] or ["please"];

    private static string[] NormalizeWords(string message)
    {
        char[] chars = message.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        string text = new string(chars);
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
        return text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
