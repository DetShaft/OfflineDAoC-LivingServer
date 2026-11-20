using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// Pure rules for the player group's fight mode (#165). The same rules hold in every realm: a target's
    /// job comes from its class data (<see cref="BotPartyRoles"/> and the class type), never from a class name.
    /// Sources: docs/PVP-BEHAVIOR-RESEARCH.md section E (question 17) and Table 4.
    /// </summary>
    public static class GroupFightPolicy
    {
        /// <summary>No hostile seen for this long ends fight mode.</summary>
        public const int ExitDelayMilliseconds = 6_000;

        /// <summary>An enemy that hit a groupmate stays a fight-mode candidate this long after its last hit.</summary>
        public const int ThreatMemoryMilliseconds = 15_000;

        /// <summary>Default chase distance from the player (server setting bots / group_fight_chase_distance).</summary>
        public const int DefaultChaseDistance = 1500;

        /// <summary>The setting is clamped to this range, so a typo cannot pin bots to the player or let them roam.</summary>
        public const int MinChaseDistance = 500, MaxChaseDistance = 3000;

        public enum TargetRole { Healer = 0, Caster = 1, Fighter = 2, Pet = 3 }

        public enum PickReason { None, Leader, Peel, Focus }

        public readonly record struct Candidate(
            TargetRole Role,
            int HealthPercent,
            int DistanceFromLeader,
            bool Mezzed,
            bool HittingBackLine);

        public static int ChaseDistance(int setting) =>
            setting <= 0 ? DefaultChaseDistance : System.Math.Clamp(setting, MinChaseDistance, MaxChaseDistance);

        /// <summary>
        /// A target's group job. Healers are the support classes and the healing hybrids that are not tanks
        /// (a Paladin is a tank first); casters are the list casters; everything else that is a character fights.
        /// </summary>
        public static TargetRole RoleOf(eCharacterClass? characterClass, bool listCaster)
        {
            if (characterClass == null) return TargetRole.Pet;
            eCharacterClass c = characterClass.Value;
            BotPartyRole role = BotPartyRoles.For(c);
            if (role == BotPartyRole.Support || BotPartyRoles.IsHealingClass(c) && role != BotPartyRole.Tank)
                return TargetRole.Healer;
            return listCaster ? TargetRole.Caster : TargetRole.Fighter;
        }

        /// <summary>
        /// The group's shared focus: never a mezzed enemy (hitting it breaks the mez); then healers, casters,
        /// fighters, pets; then the lowest health; then the closest to the player. Returns the index, or -1.
        /// </summary>
        public static int PickFocus(IReadOnlyList<Candidate> candidates)
        {
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Mezzed) continue;
                if (best < 0 || Better(candidates[i], candidates[best])) best = i;
            }
            return best;
        }

        /// <summary>A tank peels first: the free enemy hitting the group's healers or casters, closest to the player.</summary>
        public static int PickPeel(IReadOnlyList<Candidate> candidates)
        {
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate c = candidates[i];
                if (c.Mezzed || !c.HittingBackLine) continue;
                if (best < 0 || c.DistanceFromLeader < candidates[best].DistanceFromLeader) best = i;
            }
            return best;
        }

        private static bool Better(Candidate a, Candidate b)
        {
            if (a.Role != b.Role) return a.Role < b.Role;
            if (a.HealthPercent != b.HealthPercent) return a.HealthPercent < b.HealthPercent;
            return a.DistanceFromLeader < b.DistanceFromLeader;
        }

        /// <summary>Fight mode ends once no hostile has been seen for <see cref="ExitDelayMilliseconds"/>.</summary>
        public static bool ShouldExit(long now, long lastHostileSeen) => now - lastHostileSeen >= ExitDelayMilliseconds;

        /// <summary>
        /// A target past the chase distance from the player is dropped, unless the player is fighting it or it is
        /// hitting this bot or a groupmate near the player (then the fight has come to the group).
        /// </summary>
        public static bool BeyondLeash(int targetDistanceFromLeader, int chaseDistance, bool leaderTarget, bool attackingGroupNearLeader) =>
            !leaderTarget && !attackingGroupNearLeader && targetDistanceFromLeader > chaseDistance;

        /// <summary>The player's target comes first, unless it is mezzed (a groupmate's mez must hold).</summary>
        public static bool FollowsLeaderTarget(bool leaderTargetValid, bool leaderTargetMezzed) =>
            leaderTargetValid && !leaderTargetMezzed;

        public static string Label(TargetRole role) => role switch
        {
            TargetRole.Healer => "healer",
            TargetRole.Caster => "caster",
            TargetRole.Fighter => "fighter",
            _ => "pet"
        };
    }
}
