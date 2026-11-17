using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Issue #116 (owner, 2026-11-17): a player's characters on one realm share a guild and a friends list.
    /// <list type="bullet">
    /// <item><b>Guild.</b> When a character of a real player enters the game with no guild (or only the realm's starter
    /// guild), it joins the guild of that account's most recently played character on the same realm that has one,
    /// at that character's rank, so a guildmaster is guildmaster on every character. A character already in another
    /// guild stays where it is: nothing ever pulls a character out of a guild its player chose. Server setting
    /// <c>realm_alts_share_guild</c>.</item>
    /// <item><b>Friends.</b> On entering the game, a character's friends list becomes the union of its own and its realm
    /// alts' lists, and every later /friend add or remove is written to all of them. A character never lists itself.
    /// Server setting <c>realm_alts_share_friends</c>.</item>
    /// </list>
    /// Only real players' characters (GamePlayer, by account) are touched; companion bots and autonomous gamebots are
    /// GameBot NPCs and never pass through here.
    /// </summary>
    public static class RealmAltSharing
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(typeof(RealmAltSharing));

        /// <summary>What the guild sync needs to know of one realm alt.</summary>
        public readonly record struct AltGuild(string Name, string GuildID, int RankLevel, DateTime LastPlayed, bool StarterGuild);

        /// <summary>The alt whose guild a guildless character joins: the most recently played one in a real guild.</summary>
        public static AltGuild? PickGuildSource(IEnumerable<AltGuild> alts)
        {
            if (alts == null)
                return null;
            foreach (AltGuild alt in alts.OrderByDescending(a => a.LastPlayed).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(alt.GuildID) && !alt.StarterGuild)
                    return alt;
            }
            return null;
        }

        /// <summary>One shared friends list: every name once (first spelling wins), in first-seen order.</summary>
        public static string[] MergeFriends(IEnumerable<string> own, IEnumerable<IEnumerable<string>> alts)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<string>();
            foreach (IEnumerable<string> list in new[] { own }.Concat(alts ?? Enumerable.Empty<IEnumerable<string>>()))
            {
                if (list == null)
                    continue;
                foreach (string raw in list)
                {
                    string name = raw?.Trim();
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                        merged.Add(name);
                }
            }
            return merged.ToArray();
        }

        /// <summary>The list as one character stores it: without its own name.</summary>
        public static string[] ListFor(string characterName, IEnumerable<string> shared) =>
            (shared ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Trim().Equals(characterName, StringComparison.OrdinalIgnoreCase))
                .Select(name => name.Trim()).ToArray();

        public static string[] Split(string serialized) => string.IsNullOrEmpty(serialized)
            ? Array.Empty<string>()
            : serialized.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0).ToArray();

        #region Live

        [ScriptLoadedEvent]
        public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args)
        {
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));
        }

        [ScriptUnloadedEvent]
        public static void OnScriptUnloaded(DOLEvent e, object sender, EventArgs args)
        {
            GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));
        }

        private static void OnPlayerEntered(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is GamePlayer player && Properties.REALM_ALTS_SHARE_GUILD)
                JoinAltsGuild(player);
        }

        /// <summary>The account's other characters on this character's realm.</summary>
        private static List<DbCoreCharacter> RealmAlts(GamePlayer player)
        {
            if (player?.DBCharacter == null || string.IsNullOrEmpty(player.AccountName))
                return new List<DbCoreCharacter>();
            string self = player.DBCharacter.ObjectId;
            // The account's character list is loaded at login and the next character entered comes from it, so an
            // alt is changed through that same object when it is there; a fresh copy would be overwritten by it later.
            Dictionary<string, DbCoreCharacter> cached = (player.Client?.Account?.Characters ?? Array.Empty<DbCoreCharacter>())
                .Where(ch => ch != null && !string.IsNullOrEmpty(ch.ObjectId))
                .GroupBy(ch => ch.ObjectId).ToDictionary(g => g.Key, g => g.First());
            return DOLDB<DbCoreCharacter>.SelectObjects(DB.Column("AccountName").IsEqualTo(player.AccountName)
                    .And(DB.Column("Realm").IsEqualTo((int)player.Realm)))
                .Where(ch => ch != null && ch.ObjectId != self)
                .Select(ch => cached.TryGetValue(ch.ObjectId, out DbCoreCharacter live) ? live : ch).ToList();
        }

        /// <summary>A guildless character (or one in the starter guild only) joins its realm alts' guild.</summary>
        public static void JoinAltsGuild(GamePlayer player)
        {
            try
            {
                if (player?.Client?.Account == null || !AutonomousBotGuildSocial.CountsAsGuildless(player.Guild))
                    return;
                AltGuild? source = PickGuildSource(RealmAlts(player).Select(ch =>
                {
                    Guild guild = string.IsNullOrEmpty(ch.GuildID) ? null : GuildMgr.GetGuildByGuildID(ch.GuildID);
                    return new AltGuild(ch.Name, guild?.GuildID, ch.GuildRank, ch.LastPlayed, guild?.IsStartingGuild ?? false);
                }));
                if (source is not AltGuild alt)
                    return;
                Guild target = GuildMgr.GetGuildByGuildID(alt.GuildID);
                if (target == null || target.Realm != player.Realm)
                    return;
                DbGuildRank rank = target.GetRankByID(alt.RankLevel) ?? target.GetRankByID(9);
                player.Guild?.RemovePlayer(player.Name, player);
                if (target.AddPlayer(player, rank))
                {
                    player.Out.SendMessage($"Your characters on this realm share a guild: {player.Name} joins {target.Name}, like {alt.Name}.",
                        eChatType.CT_System, eChatLoc.CL_SystemWindow);
                }
            }
            catch (Exception ex)
            {
                log.Warn($"RealmAltSharing.JoinAltsGuild(): {player?.Name}: {ex.Message}");
            }
        }

        /// <summary>On entering the game: the character's list becomes the realm's shared list (FriendsManager calls this first).</summary>
        public static void MergeFriendsOnEnter(GamePlayer player)
        {
            if (!Properties.REALM_ALTS_SHARE_FRIENDS || player?.DBCharacter == null)
                return;
            try
            {
                List<DbCoreCharacter> alts = RealmAlts(player);
                if (alts.Count == 0)
                    return;
                string[] shared = MergeFriends(player.SerializedFriendsList, alts.Select(ch => Split(ch.SerializedFriendsList)));
                player.SerializedFriendsList = ListFor(player.Name, shared);
                WriteToAlts(alts, shared);
            }
            catch (Exception ex)
            {
                log.Warn($"RealmAltSharing.MergeFriendsOnEnter(): {player?.Name}: {ex.Message}");
            }
        }

        /// <summary>After /friend add or remove: the same list on every realm alt.</summary>
        public static void ShareFriends(GamePlayer player, IEnumerable<string> list)
        {
            if (!Properties.REALM_ALTS_SHARE_FRIENDS || player?.DBCharacter == null)
                return;
            try
            {
                WriteToAlts(RealmAlts(player), list.ToArray());
            }
            catch (Exception ex)
            {
                log.Warn($"RealmAltSharing.ShareFriends(): {player?.Name}: {ex.Message}");
            }
        }

        private static void WriteToAlts(IEnumerable<DbCoreCharacter> alts, string[] shared)
        {
            foreach (DbCoreCharacter alt in alts)
            {
                string value = string.Join(",", ListFor(alt.Name, shared));
                if (string.Equals(alt.SerializedFriendsList ?? string.Empty, value, StringComparison.Ordinal))
                    continue;
                alt.SerializedFriendsList = value;
                GameServer.Database.SaveObject(alt);
            }
        }

        #endregion
    }
}
