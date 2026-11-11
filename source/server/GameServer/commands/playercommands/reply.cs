using System;
using System.Collections.Concurrent;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /reply (/r): answers the last private message you were sent. The server had no /r, and "/r" matched several
    /// commands that start with r, so the new client's reply (its Reply key types "/r ") did nothing (owner
    /// 2026-11-11: "a reply mechanic to private messages, it currently doesnt work"). The reply is the /send it stands
    /// for, so it reaches players, autonomous gamebots (who answer it) and the realm's gold spammer the same way.
    /// </summary>
    [CmdAttribute(
        "&reply",
        new [] { "&r" },
        ePrivLevel.Player,
        "Replies to the last private message you received.",
        "/reply <message> or /r <message>")]
    public class ReplyCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null)
                return;

            if (!TellReplies.TryGetLastSender(client.Player.Name, out string sender))
            {
                client.Out.SendMessage("No one has sent you a private message yet.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (args.Length < 2)
            {
                client.Out.SendMessage($"Usage: /reply <message> (or /r) - replies to {sender}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            new SendCommandHandler().OnCommand(client, TellReplies.SendArgs(sender, args));
        }
    }

    /// <summary>Who last sent each player a private message (by character name), for /reply.</summary>
    public static class TellReplies
    {
        private static readonly ConcurrentDictionary<string, string> LastSender = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Notes that <paramref name="senderName"/> just sent <paramref name="recipientName"/> a private message.</summary>
        public static void Record(string recipientName, string senderName)
        {
            if (string.IsNullOrWhiteSpace(recipientName) || string.IsNullOrWhiteSpace(senderName))
                return;
            if (string.Equals(recipientName.Trim(), senderName.Trim(), StringComparison.OrdinalIgnoreCase))
                return;
            LastSender[recipientName.Trim()] = senderName.Trim();
        }

        public static void Record(GamePlayer recipient, string senderName) => Record(recipient?.Name, senderName);

        public static bool TryGetLastSender(string recipientName, out string senderName)
        {
            senderName = null;
            return !string.IsNullOrWhiteSpace(recipientName) && LastSender.TryGetValue(recipientName.Trim(), out senderName);
        }

        /// <summary>The /send arguments a /reply stands for: "&amp;send", the sender, then the reply's words.</summary>
        public static string[] SendArgs(string senderName, string[] replyArgs)
        {
            int words = Math.Max(0, (replyArgs?.Length ?? 0) - 1);
            string[] send = new string[words + 2];
            send[0] = "&send";
            send[1] = senderName;
            if (words > 0)
                Array.Copy(replyArgs, 1, send, 2, words);
            return send;
        }

        /// <summary>Tests only.</summary>
        public static void Clear() => LastSender.Clear();
    }
}
