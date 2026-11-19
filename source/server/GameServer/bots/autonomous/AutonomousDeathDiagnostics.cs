using System;
using System.Linq;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// One server-log line per autonomous gamebot death (AUTONOMOUS_BOT_DEATH): what it was doing, what killed it and
    /// how many were on it. The overnight log of 2026-10-19 had 3,553 deaths and nothing that named a killer, so the
    /// cause of the half that happen on the way to a camp could only be guessed.
    /// </summary>
    public static class AutonomousDeathDiagnostics
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousDeathDiagnostics));

        /// <summary>The killer's kind: a monster, a pet, an enemy bot, a real player or something else.</summary>
        public static string KillerKind(GameObject killer) => killer switch
        {
            null => "none",
            GamePlayer => "player",
            GameBot => "bot",
            GameNPC { Brain: DOL.AI.Brain.IControlledBrain } => "pet",
            GameNPC => "monster",
            _ => killer.GetType().Name,
        };

        /// <summary>
        /// The killer as a death is told in chat (1.1.3, after RC6): "guard" for a keep or town guard, "pet" for the pet of a
        /// player or a bot, "monster" for a monster or a pet that is no one's (a charmed or summoned creature without a player or
        /// bot owner), else <see cref="KillerKind"/> ("bot", "player", "none").
        /// </summary>
        public static string ChatKillerKind(GameObject killer) => killer switch
        {
            // RC7 on the owner's PC: a gamebot's brain is a controlled brain with no owner, so an enemy bot read as a monster
            // ("adds in murdaigean hate me" after one enemy friar). A bot is a bot before anything else.
            GameBot => "bot",
            DOL.GS.Keeps.GameKeepGuard or GameGuard => "guard",
            GameNPC { Brain: DOL.AI.Brain.IControlledBrain brain } => brain.GetLivingOwner() is GamePlayer or GameBot ? "pet" : "monster",
            _ => KillerKind(killer),
        };

        /// <summary>The class of the enemy behind a kill (the player or bot, or a pet's owner), or null.</summary>
        public static string KillerClassName(GameObject killer)
        {
            GameLiving who = killer is not GameBot && killer is GameNPC { Brain: DOL.AI.Brain.IControlledBrain brain } ? brain.GetLivingOwner() : killer as GameLiving;
            return who switch
            {
                GamePlayer player => player.CharacterClass?.Name,
                GameBot bot => bot.CharacterClass?.Name ?? bot.ClassName,
                _ => null,
            };
        }

        /// <summary>How many were on the bot (its attacker tracker), read before the death clears it.</summary>
        public static int Attackers(GameBot bot)
        {
            try
            {
                return bot?.attackComponent?.AttackerTracker?.Attackers?.Count() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// What a bot in a real player's group is doing, for its death line and dashboards. Such a bot runs none of its
        /// own goals (the autonomous controller skips it), so its stored activity is whatever it last did alone. The
        /// Jarl Killaloe deaths of 2026-11-19 read "Leaving the battleground" and "Teleporting to Caer Ulfwych" while the
        /// bots were in Ferac's group fighting the lord.
        /// </summary>
        public static string PlayerGroupActivity(string leaderName, string targetName) =>
            string.IsNullOrEmpty(targetName) ? $"In {leaderName}'s group" : $"Fighting {targetName} in {leaderName}'s group";

        /// <summary>
        /// One line per death of a bot in a real player's group (PLAYER_GROUP_BOT_DEATH): where every group member was
        /// and what it was doing. The 2026-11-19 lord fight lost three bots one at a time while the group's cleric stood
        /// 734 units away, out of combat, and nothing in the log said where the rest of the group was.
        /// </summary>
        public static void RecordPlayerGroup(GameBot bot, GameObject killer)
        {
            if (bot?.IsPlayerLedGroup != true || bot.Group == null || !Log.IsInfoEnabled)
                return;
            try
            {
                string members = string.Join(";", bot.Group.GetMembersInTheGroup().Where(m => m != null && m != bot).Select(m =>
                {
                    bool here = m.CurrentRegion == bot.CurrentRegion;
                    string state = m is GameNPC { Brain: var brain } ? brain?.FSM?.GetCurrentState()?.StateType.ToString() ?? "-" : "player";
                    return $"{m.Name}:alive={m.IsAlive}:dist={(here ? m.GetDistanceTo(bot).ToString() : "other-region")}:" +
                           $"dz={(here ? (m.Z - bot.Z).ToString() : "-")}:combat={m.InCombat}:casting={m.IsCasting}:" +
                           $"target={(m.TargetObject as GameLiving)?.Name ?? "-"}:state={state}";
                }));
                Log.Info($"PLAYER_GROUP_BOT_DEATH bot=\"{bot.Name}\" id={bot.DatabaseID} class=\"{bot.ClassName}\" level={bot.Level} " +
                         $"leader=\"{bot.PlayerGroupLeader?.Name}\" killer=\"{killer?.Name}\" killerLevel={(killer as GameLiving)?.Level ?? 0} " +
                         $"target=\"{(bot.TargetObject as GameLiving)?.Name}\" region={bot.CurrentRegionID} at={bot.X},{bot.Y},{bot.Z} " +
                         $"members=\"{members}\"");
            }
            catch (Exception exception)
            {
                Log.Warn("PLAYER_GROUP_BOT_DEATH could not be written", exception);
            }
        }

        public static void Record(GameBot bot, GameObject killer, string activityBefore)
        {
            if (bot?.IsAutonomousWorldBot != true)
                return;
            if (killer is GameLiving deadly && AutonomousPveBossDanger.Marks(KillerKind(killer), deadly.Level, bot.Level))
                AutonomousPveBossDanger.Record(bot.CurrentRegionID, bot.X, bot.Y, deadly.Level, DateTime.UtcNow);
            if (string.Equals(KillerKind(killer), "monster", StringComparison.Ordinal))
                AutonomousRepeatDeathSpots.Record(bot.DatabaseID, bot.CurrentRegionID, bot.X, bot.Y, DateTime.UtcNow);
            if (!Log.IsInfoEnabled)
                return;
            try
            {
                GameLiving living = killer as GameLiving;
                int attackers = Attackers(bot);
                string con = living != null ? ConLevels.GetConColor(bot.GetConLevel(living)).ToString() : "-";
                bool arrived = bot.GoalDiagnosticAttempt?.ArrivedUtc != null;
                int armor = bot.Inventory?.EquippedItems?.Count(item => item != null && item.Object_Type >= (int)eObjectType._FirstArmor &&
                    item.Object_Type <= (int)eObjectType._LastArmor) ?? -1;
                Log.Info($"AUTONOMOUS_BOT_DEATH bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} class=\"{bot.ClassName}\" " +
                         $"realm={GlobalConstants.RealmToName(bot.Realm)} group={(bot.Group?.MemberCount ?? 1)} " +
                         $"killer=\"{killer?.Name}\" killerKind={KillerKind(killer)} killerLevel={living?.Level ?? 0} con={con} " +
                         $"attackers={attackers} armorPieces={armor} maxHealth={bot.MaxHealth} arrivedAtCamp={arrived} " +
                         $"activity=\"{activityBefore}\" goal=\"{bot.PersistentRecord?.CurrentGoal}\" " +
                         $"region={bot.CurrentRegionID} zone=\"{bot.CurrentZone?.Description}\" at={bot.X},{bot.Y},{bot.Z}");
            }
            catch (Exception exception)
            {
                Log.Warn("AUTONOMOUS_BOT_DEATH could not be written", exception);
            }
        }
    }
}
