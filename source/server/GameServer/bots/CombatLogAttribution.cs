using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Combat log attribution for bots that play a character (owner 2026-11-16: "Spawned bots abilities, skills, spells,
    /// damage etc registers under you in the combat log"). A <see cref="GameBot"/> uses a brain that implements
    /// <c>IControlledBrain</c> with the player as its owner, so the pet code paths sent the bot's own first-person lines
    /// ("You hit the goblin for 50 damage!", "You perform your Doublefrost perfectly!") to the player, who read them as
    /// their own. A bot's lines now name the bot ("Aldwin hits the goblin for 50 damage!") and go to the player's
    /// OTHERS' COMBAT channel (<see cref="eChatType.CT_OthersCombat"/>), the way another player's fighting shows.
    /// By default (owner 2026-11-16: "Drop them by default") the owner sees only what live shows for another player:
    /// "Aldwin attacks the goblin with his sword!", with no damage numbers, crits, styles or spell lines. The server
    /// setting <c>bots / bot_combat_log_details</c> brings those back, still named after the bot. Real pets are unchanged.
    /// </summary>
    public static class CombatLogAttribution
    {
        /// <summary>The channel a bot's combat lines go to on its owner's log.</summary>
        public const eChatType Channel = eChatType.CT_OthersCombat;

        /// <summary>Whether owners see their bots' damage numbers, crits, styles and spell lines (off by default).</summary>
        public static bool ShowDetails => ServerProperties.Properties.BOT_COMBAT_LOG_DETAILS;

        /// <summary>Whether <paramref name="living"/> is a bot playing a character, not a pet.</summary>
        public static bool IsCharacterBot(GameLiving living) => living is GameBot;

        /// <summary>Sends a bot's combat line, already in the third person, to its player owner.</summary>
        public static void SendToOwner(GamePlayer owner, string message)
        {
            if (owner != null && !string.IsNullOrEmpty(message))
                owner.Out.SendMessage(message, Channel, eChatLoc.CL_SystemWindow);
        }

        /// <summary>Rewrites a bot's first-person line about itself in the third person, using its gendered pronouns.</summary>
        public static string ThirdPerson(GameBot bot, string message) =>
            bot == null ? message : ThirdPerson(message, bot.Name, bot.GetPronoun(1, false), bot.GetPronoun(2, false));

        private static readonly Dictionary<string, string> Irregular = new(StringComparer.Ordinal)
        {
            ["are"] = "is", ["were"] = "was", ["have"] = "has", ["do"] = "does", ["don't"] = "doesn't",
            ["aren't"] = "isn't", ["weren't"] = "wasn't", ["haven't"] = "hasn't",
            ["can"] = "can", ["can't"] = "can't", ["cannot"] = "cannot", ["could"] = "could", ["must"] = "must",
            ["will"] = "will", ["won't"] = "won't", ["may"] = "may", ["might"] = "might", ["should"] = "should",
            ["already"] = "already", ["successfully"] = "successfully", ["critically"] = "critically",
        };

        private static readonly Regex SecondPerson = new(@"\b(yourself|your|you)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Rewrites a first-person combat line in the third person. Pure.
        /// "You hit X for 5 damage!" becomes "Name hits X for 5 damage!", "Your Name hits X" becomes "Name hits X",
        /// "Your Fireball hits X" becomes "Name's Fireball hits X", and any later "your", "you" or "yourself" becomes
        /// the bot's own pronoun. A line without a first-person subject ("The goblin resists the effect!") keeps its
        /// words and gets the bot's name added in brackets, unless it already names the bot.
        /// </summary>
        public static string ThirdPerson(string message, string name, string possessive = "their", string objective = "them")
        {
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(name))
                return message;

            string head;
            string rest;

            if (message.StartsWith("Your " + name + " ", StringComparison.Ordinal))
            {
                head = name;
                rest = message.Substring(5 + name.Length);
            }
            else if (message.StartsWith("Your ", StringComparison.Ordinal))
            {
                head = name + "'s";
                rest = message.Substring(4);
            }
            else if (message.StartsWith("You ", StringComparison.Ordinal))
            {
                string afterYou = message.Substring(4);
                int end = 0;

                while (end < afterYou.Length && (char.IsLetter(afterYou[end]) || afterYou[end] == '\''))
                    end++;

                string verb = afterYou.Substring(0, end);
                string tail = afterYou.Substring(end);
                string conjugated = Conjugate(verb);

                // "You critically hit" -> "Name critically hits": an adverb keeps its form, the verb after it changes.
                if (conjugated == verb && end < afterYou.Length && afterYou[end] == ' ' && Irregular.ContainsKey(verb) && IsAdverb(verb))
                {
                    string nextPart = tail.Substring(1);
                    int nextEnd = 0;

                    while (nextEnd < nextPart.Length && char.IsLetter(nextPart[nextEnd]))
                        nextEnd++;

                    tail = " " + Conjugate(nextPart.Substring(0, nextEnd)) + nextPart.Substring(nextEnd);
                }

                head = name + " " + conjugated;
                rest = tail;
            }
            else
            {
                if (message.Contains(name, StringComparison.Ordinal))
                    return message;

                return message.TrimEnd() + " (" + name + ")";
            }

            return head + ReplaceSecondPerson(rest, possessive, objective);
        }

        private static bool IsAdverb(string word) => word is "already" or "successfully" or "critically";

        private static string ReplaceSecondPerson(string text, string possessive, string objective) =>
            SecondPerson.Replace(text, match =>
            {
                string word = match.Value.ToLowerInvariant();
                string replacement = word switch
                {
                    "yourself" => Reflexive(objective),
                    "your" => possessive,
                    _ => objective,
                };

                return char.IsUpper(match.Value[0]) ? char.ToUpperInvariant(replacement[0]) + replacement.Substring(1) : replacement;
            });

        private static string Reflexive(string objective) => objective switch
        {
            "him" => "himself",
            "her" => "herself",
            "it" => "itself",
            _ => "themselves",
        };

        /// <summary>Third person singular of an English verb ("hit" -> "hits", "steal" -> "steals", "are" -> "is"). Pure.</summary>
        public static string Conjugate(string verb)
        {
            if (string.IsNullOrEmpty(verb))
                return verb;

            if (Irregular.TryGetValue(verb, out string irregular))
                return irregular;

            if (verb.EndsWith("ss") || verb.EndsWith("sh") || verb.EndsWith("ch") || verb.EndsWith("x") || verb.EndsWith("z") ||
                verb.EndsWith("o"))
                return verb + "es";

            if (verb.Length > 1 && verb.EndsWith("y") && !"aeiou".Contains(verb[^2]))
                return verb.Substring(0, verb.Length - 1) + "ies";

            return verb + "s";
        }

        /// <summary>
        /// The owner's log lines for a bot's melee or ranged attack, in place of the pet lines ("Your Aldwin attacks ...",
        /// "You perform your ... perfectly!").
        /// </summary>
        public static void SendAttackerLines(GameBot bot, AttackData ad)
        {
            GamePlayer owner = bot?.Owner;

            if (owner == null || ad == null)
                return;

            foreach (string line in AttackerLines(bot.Name, ad.Target?.GetName(0, false), ad.AttackResult, ad.AttackType,
                ad.Damage, ad.Modifier, ad.CriticalDamage, ad.CriticalChance, ad.Style?.Name, ad.StyleDamage, ad.Style?.GrowthRate ?? 0,
                ad.BroadcastMessage, ad.MissChance, ShowDetails))
                SendToOwner(owner, line);
        }

        /// <summary>The owner's log lines for an attack on a bot (melee, ranged, or spell damage), without "your".</summary>
        public static void SendTargetLines(GameBot bot, AttackData ad)
        {
            GamePlayer owner = bot?.Owner;

            if (owner == null || ad == null)
                return;

            foreach (string line in TargetLines(ad.Attacker?.GetName(0, true), bot.Name, ad.AttackResult, ad.AttackType,
                ad.Damage, ad.Modifier, ad.CriticalDamage, ad.BroadcastMessage, ShowDetails))
                SendToOwner(owner, line);
        }

        private static string Modifier(int modifier) =>
            modifier > 0 ? $" (+{modifier})" : modifier < 0 ? $" ({modifier})" : string.Empty;

        /// <summary>The lines naming a bot as the attacker. Pure.</summary>
        public static List<string> AttackerLines(string bot, string target, eAttackResult result, AttackData.eAttackType type,
            int damage, int modifier, int criticalDamage, int criticalChance, string styleName, int styleDamage, double growthRate,
            string broadcast, double missChance, bool details = true)
        {
            List<string> lines = new();

            // Live: another player's fighting shows only as the area line ("Aldwin attacks the goblin with his sword!").
            if (!details)
            {
                if (!string.IsNullOrEmpty(broadcast))
                    lines.Add(broadcast);

                return lines;
            }

            switch (result)
            {
                case eAttackResult.HitStyle:
                case eAttackResult.HitUnstyled:
                {
                    if (result is eAttackResult.HitStyle && !string.IsNullOrEmpty(styleName))
                        lines.Add($"{bot} performs {styleName} perfectly! (+{styleDamage}, GR: {growthRate})");

                    string verb = type is AttackData.eAttackType.Ranged ? "shoots" : "attacks";
                    lines.Add($"{bot} {verb} {target} and hits for {damage}{Modifier(modifier)} damage!");

                    if (criticalDamage > 0)
                        lines.Add($"{bot} critically hits {target} for an additional {criticalDamage} damage! ({criticalChance}%)");

                    break;
                }
                case eAttackResult.Missed:
                {
                    if (!string.IsNullOrEmpty(broadcast))
                        lines.Add($"{broadcast} ({missChance:0.##}%)");

                    break;
                }
                default:
                {
                    if (!string.IsNullOrEmpty(broadcast))
                        lines.Add(broadcast);

                    break;
                }
            }

            return lines;
        }

        /// <summary>The lines naming a bot as the one attacked. Pure.</summary>
        public static List<string> TargetLines(string attacker, string bot, eAttackResult result, AttackData.eAttackType type,
            int damage, int modifier, int criticalDamage, string broadcast = null, bool details = true)
        {
            List<string> lines = new();

            // Live: an attack on another player shows only as the area line, and spell damage not at all.
            if (!details)
            {
                if (type is not AttackData.eAttackType.Spell && !string.IsNullOrEmpty(broadcast))
                    lines.Add(broadcast);

                return lines;
            }

            switch (result)
            {
                case eAttackResult.Blocked:
                    lines.Add($"{attacker} attacks {bot} and is blocked!");
                    break;
                case eAttackResult.Parried:
                    lines.Add($"{attacker} attacks {bot} and is parried!");
                    break;
                case eAttackResult.Evaded:
                    lines.Add($"{attacker} attacks {bot} and is evaded!");
                    break;
                case eAttackResult.Fumbled:
                    lines.Add($"{attacker} fumbled!");
                    break;
                case eAttackResult.Missed:
                    if (type is not AttackData.eAttackType.Spell)
                        lines.Add($"{attacker} attacks {bot} and misses!");
                    break;
                case eAttackResult.HitStyle:
                case eAttackResult.HitUnstyled:
                    if (damage > 0 || type is not AttackData.eAttackType.Spell)
                    {
                        lines.Add($"{attacker} hits {bot} for {damage}{Modifier(modifier)} damage.");

                        if (criticalDamage > 0)
                            lines.Add($"{attacker} critically hits {bot} for an additional {criticalDamage} damage.");
                    }
                    break;
            }

            return lines;
        }
    }
}
