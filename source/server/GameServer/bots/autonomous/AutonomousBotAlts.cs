using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Alts for autonomous gamebots (issue #57). A bot "player" is its first character (OfflineWorldBotRecord.PlayerId 0)
/// plus the alts it rolls later (PlayerId = that first character's BotId). Once all of a player's characters are level
/// 50, it rolls a new level-one alt in the same realm, so the leveling zones stay populated as the roster ages.
/// <list type="bullet">
/// <item>Only one of a player's characters is online at a time. The player's schedule (clock or session) is the first
/// character's, so the roster's online numbers don't change: alts add characters, not players.</item>
/// <item>Most of the player's time goes to its leveling character. In a share of its two-hour blocks
/// (<c>bot_alts_main_share</c>) it plays one of its level-50s instead, for RvR and end-game content.</item>
/// <item>All of a player's characters share its personality (AutonomousBotPersonality reads the player's id).</item>
/// <item>Needs a schedule (<c>bot_schedule_mode</c> session or clock): with the whole roster always online there is
/// no time to share between characters, so no alts are rolled.</item>
/// </list>
/// Companion bots, /spawn helpers, befriended helpers' first hour and Epic Special Forces are not players here: only
/// roster records take part. Real players are never affected.
/// </summary>
public static class AutonomousBotAlts
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public const int MaxLevel = 50;

    /// <summary>The longest a player's character list may grow (its first character and its alts).</summary>
    public const int MaxCharactersPerPlayer = 6;

    /// <summary>Alts rolled per roster refresh at most, so a roster full of level-50s grows its alts over hours.</summary>
    public const int MaxNewAltsPerRefresh = 3;

    /// <summary>The length of one play block, in hours: which of a player's characters plays changes only between blocks.</summary>
    public const int BlockHours = 2;

    public static bool Enabled => Properties.BOT_ALTS_ENABLED && AutonomousBotSchedule.CurrentMode != AutonomousBotSchedule.Mode.Off;

    public static double MainShare => Math.Clamp(Properties.BOT_ALTS_MAIN_SHARE, 0.0, 1.0);

    /// <summary>One character of a player, as the roster has it.</summary>
    public readonly record struct Character(long BotId, int Level);

    private sealed class Index
    {
        public readonly Dictionary<long, long> PlayerOf = new();
        public readonly Dictionary<long, Character[]> Characters = new();
    }

    private static volatile Index _index = new();

    #region The roster index

    /// <summary>The player a roster character belongs to: its own id for a first character or an unknown id.</summary>
    public static long PlayerOf(long botId)
    {
        if (botId <= 0)
            return botId;
        return _index.PlayerOf.TryGetValue(botId, out long player) ? player : botId;
    }

    /// <summary>The player's characters, oldest first (only the one character when it has no alts).</summary>
    public static IReadOnlyList<Character> CharactersOf(long playerId) =>
        _index.Characters.TryGetValue(playerId, out Character[] characters) ? characters : [];

    /// <summary>Rebuilds the index from the roster (the population controller's refresh, off the game loop).</summary>
    public static void Refresh(IEnumerable<OfflineWorldBotRecord> roster)
    {
        var index = new Index();
        var lists = new Dictionary<long, List<Character>>();
        foreach (OfflineWorldBotRecord record in roster ?? [])
        {
            if (record == null || record.IsRetired)
                continue;
            long player = PlayerIdOf(record);
            index.PlayerOf[record.BotId] = player;
            if (!lists.TryGetValue(player, out List<Character> list))
                lists[player] = list = new List<Character>();
            list.Add(new Character(record.BotId, record.Level));
        }
        foreach ((long player, List<Character> list) in lists)
            index.Characters[player] = list.OrderBy(character => character.BotId).ToArray();
        _index = index;
    }

    /// <summary>The player of a record: its PlayerId, or itself when it is a first character.</summary>
    public static long PlayerIdOf(OfflineWorldBotRecord record) => record.PlayerId > 0 ? record.PlayerId : record.BotId;

    #endregion

    #region Who plays (pure, unit tested)

    /// <summary>The play block of a local time: two-hour blocks counted from a fixed day.</summary>
    public static long BlockOf(DateTime local) => (long)(local.Date - new DateTime(2000, 1, 1)).TotalDays * (24 / BlockHours) + local.Hour / BlockHours;

    /// <summary>
    /// Which of a player's characters plays in this block (pure). The leveling character is the newest one below level
    /// 50. In <paramref name="mainShare"/> of the blocks, picked by a stable draw on the player's id, one of the level-50s
    /// plays instead (a different one from block to block when it has several). With no leveling character the
    /// level-50s take turns; a player with one character always plays it.
    /// </summary>
    public static long Playing(long playerId, IReadOnlyList<Character> characters, long block, double mainShare)
    {
        if (characters == null || characters.Count == 0)
            return playerId;
        if (characters.Count == 1)
            return characters[0].BotId;
        Character[] fifties = characters.Where(character => character.Level >= MaxLevel).OrderBy(character => character.BotId).ToArray();
        Character? leveling = characters.Where(character => character.Level < MaxLevel)
            .OrderByDescending(character => character.BotId).Select(character => (Character?)character).FirstOrDefault();
        Character Fifty() => fifties[(int)(AutonomousBotSchedule.Mix(playerId, 6000 + block) % (ulong)fifties.Length)];
        if (leveling == null)
            return Fifty().BotId;
        if (fifties.Length > 0 && AutonomousBotSchedule.Unit(playerId, 5000 + block) < mainShare)
            return Fifty().BotId;
        return leveling.Value.BotId;
    }

    /// <summary>
    /// Whether a player rolls a new alt (pure): alts are on, every one of its characters is level 50 and it has room
    /// for another character.
    /// </summary>
    public static bool WantsNewAlt(IReadOnlyList<Character> characters, bool enabled) =>
        enabled && characters != null && characters.Count > 0 && characters.Count < MaxCharactersPerPlayer &&
        characters.All(character => character.Level >= MaxLevel);

    #endregion

    #region Live checks

    /// <summary>Whether this roster character is the one its player plays now (true for players without alts).</summary>
    public static bool IsPlaying(long botId, DateTime nowUtc)
    {
        long player = PlayerOf(botId);
        IReadOnlyList<Character> characters = CharactersOf(player);
        if (characters.Count <= 1)
            return true;
        return Playing(player, characters, BlockOf(nowUtc.ToLocalTime()), MainShare) == botId;
    }

    /// <summary>
    /// Whether another character of the same player is online or logging in, so this one waits (one character at a
    /// time; the other may still be finishing a fight or a group before it logs out).
    /// </summary>
    public static bool AnotherCharacterOnline(long botId, Func<long, bool> onlineOrPending)
    {
        IReadOnlyList<Character> characters = CharactersOf(PlayerOf(botId));
        foreach (Character character in characters)
            if (character.BotId != botId && onlineOrPending(character.BotId))
                return true;
        return false;
    }

    #endregion

    #region Rolling alts (the population controller's refresh, off the game loop)

    /// <summary>
    /// Rolls new alts for players whose characters are all level 50, at most <see cref="MaxNewAltsPerRefresh"/> a call.
    /// Each alt goes through the normal path for a new gamebot: a roster row at level one with no position, which the
    /// population controller places at a valid Classic/SI start for its race and class on login, with no armor and no
    /// coin (gamebots start without armor and earn it). True when a row was added.
    /// </summary>
    public static bool RollAlts(IReadOnlyList<OfflineWorldBotRecord> roster)
    {
        if (!Enabled || roster == null || roster.Count == 0)
            return false;
        var byId = roster.Where(record => record != null).ToDictionary(record => record.BotId);
        var reserved = new HashSet<string>(roster.Where(record => record != null).Select(record => record.Name), StringComparer.OrdinalIgnoreCase);
        int added = 0;
        bool playerNamesRead = false;
        foreach ((long player, Character[] characters) in _index.Characters.OrderBy(entry => entry.Key))
        {
            if (added >= MaxNewAltsPerRefresh)
                break;
            if (!WantsNewAlt(characters, true) || !byId.TryGetValue(player, out OfflineWorldBotRecord first))
                continue;
            if (AutonomousBeggars.IsBeggarRecord(first))
                continue;                                              // beggars live in their capital and never level
            try
            {
                if (!playerNamesRead)
                {
                    foreach (string name in GameServer.Database.SelectAllObjects<DOL.Database.DbCoreCharacter>().Select(c => c.Name))
                        reserved.Add(name);                            // real characters' names too, once per refresh
                    playerNamesRead = true;
                }
                OfflineWorldBotRecord alt = NewAlt(first, reserved, Random.Shared);
                bool saved;
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                    saved = GameServer.Database.AddObject(alt);
                if (!saved || alt.BotId <= 0)
                {
                    Log.Warn($"LIVING_SERVER_ALT_NOT_SAVED player=\"{first.Name}\" id={first.BotId}");
                    continue;
                }
                reserved.Add(alt.Name);
                added++;
                Log.Info($"LIVING_SERVER_ALT player=\"{first.Name}\" player_id={first.BotId} alt=\"{alt.Name}\" id={alt.BotId} " +
                         $"class=\"{alt.ClassName}\" race=\"{alt.RaceName}\" realm={alt.Realm} characters={characters.Length + 1}");
            }
            catch (Exception exception)
            {
                Log.Error($"Could not roll an alt for the gamebot {first.Name}", exception);
            }
        }
        return added > 0;
    }

    /// <summary>A new level-one alt of the player, in its realm, with a fresh name, class and race.</summary>
    public static OfflineWorldBotRecord NewAlt(OfflineWorldBotRecord first, ISet<string> reservedNames, Random random)
    {
        eGender gender = random.Next(2) == 0 ? eGender.Male : eGender.Female;
        AutonomousBotIdentityGenerator.Identity identity =
            AutonomousBotIdentityGenerator.Generate((eRealm)first.Realm, gender, reservedNames, random);
        string now = DateTime.UtcNow.ToString("O");
        return new OfflineWorldBotRecord
        {
            PlayerId = PlayerIdOf(first),
            Name = identity.Name,
            Realm = (int)identity.Realm,
            ClassId = (int)identity.CharacterClass,
            ClassName = identity.CharacterClass.ToString(),
            RaceId = (int)identity.Race,
            RaceName = identity.Race.ToString(),
            Gender = (int)identity.Gender,
            Level = 1,
            Experience = 0,
            RealmPoints = 0,
            ZoneName = "Classic starting area",
            Activity = "Queued for staggered login",
            CurrentGoal = "Awaiting staggered login queue",
            ObjectiveProgress = $"New alt of {first.Name}",
            IsOnline = false,
            IsAlive = true,
            IsRetired = false,
            Health = 1,
            LastUpdateUtc = now,
            LastMeaningfulProgressUtc = now,
            PreferredSpecType = -1,
        };
    }

    #endregion
}
