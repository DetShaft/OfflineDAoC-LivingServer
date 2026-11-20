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

        /// <summary>
        /// #170: a sticky focus gives way only to an enemy of a better role (a healer over a caster, a caster over a
        /// fighter), so the group turns to the back line once it is in reach but never flips between equals.
        /// </summary>
        public static bool ShouldReplaceFocus(Candidate current, Candidate best) => current.Mezzed || best.Role < current.Role;

        /// <summary>
        /// #170: a bot already fighting a live target takes the group focus when the focus is of a better role. Before,
        /// a bot with any target kept it and never reached the healer fight mode had picked.
        /// </summary>
        public static bool ShouldSwitchToFocus(TargetRole current, TargetRole focus) => focus < current;

        /// <summary>
        /// #170: the anchor when the player is dead: the living groupmate nearest the player's body. Returns the index,
        /// or -1 when nobody is alive. <paramref name="alive"/> and <paramref name="distanceToLeader"/> run in step.
        /// </summary>
        public static int AnchorIndex(IReadOnlyList<bool> alive, IReadOnlyList<int> distanceToLeader)
        {
            int best = -1;
            for (int i = 0; i < alive.Count; i++)
                if (alive[i] && (best < 0 || distanceToLeader[i] < distanceToLeader[best])) best = i;
            return best;
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

        /// <summary>
        /// #175: an enemy pet does not take a bot off its fight-mode pick. Before, a pet that hit a melee bot became its
        /// target for 5 s after every hit (BotBrain.AttackMostWanted's direct attacker), so melee spent fights on pets
        /// while the enemy players stood free. A pet is still fought when nothing better is picked, and a tank's
        /// protection of the back line is left alone. True: stay on <paramref name="pick"/>.
        /// </summary>
        public static bool PetYields(TargetRole other, TargetRole pick) => other == TargetRole.Pet && pick != TargetRole.Pet;

        /// <summary>
        /// #176: an enemy that went behind terrain stays a fight-mode candidate this long after the player last saw it.
        /// Before, a ridge between the player and the focus dropped it from the candidates at once, the focus went
        /// empty or flipped, and idle bots stood until the enemy came back into view.
        /// </summary>
        public const int SightMemoryMilliseconds = 6_000;

        /// <summary>#176: seen now, or seen within <see cref="SightMemoryMilliseconds"/> (<paramref name="lastSeen"/> &lt; 0: never).</summary>
        public static bool StillInSight(bool visibleNow, long now, long lastSeen) =>
            visibleNow || lastSeen >= 0 && now - lastSeen < SightMemoryMilliseconds;

        /// <summary>
        /// #177: the second leash. A target farther than the chase distance from the group's nearest caster or healer is
        /// dropped too, with the same exemptions as <see cref="BeyondLeash"/> (the player's target, or it is hitting the
        /// group). Jay's numbers (2026-11-20): with only the player leash, casters ~1,000 behind the player and a melee bot
        /// chasing 1,500 the other way ended up ~2,500 from them, too far for a peel, heal or mez.
        /// <paramref name="distanceFromBackLine"/> is <see cref="int.MaxValue"/> when the group has no back line in reach.
        /// </summary>
        public static bool BeyondSupport(int distanceFromBackLine, int chaseDistance, bool leaderTarget, bool attackingGroupNearLeader) =>
            !leaderTarget && !attackingGroupNearLeader && distanceFromBackLine != int.MaxValue && distanceFromBackLine > chaseDistance;

        /// <summary>#177: a caster farther than this from the nearest fighting melee groupmate closes up.</summary>
        public const int CasterReach = 1_200;

        /// <summary>#177: and stops this far from it, still out of the melee's fight.</summary>
        public const int CasterHold = 900;

        /// <summary>
        /// #177: a ranged caster that is not casting and not under attack closes up on the nearest groupmate fighting in
        /// melee, so it can peel, mez or nuke for it in time. A caster under attack keeps its kite rules (#136), and one
        /// with an enemy in spell range and in sight keeps casting: walking then cost the group its damage and mezzes
        /// (harness, los-ridge: 6 of 18 wins fell to 0, mezzer damage to a quarter).
        /// <paramref name="distanceToMelee"/> is <see cref="int.MaxValue"/> when no groupmate is fighting in melee.
        /// </summary>
        public static bool CasterClosesIn(bool casting, bool underAttack, bool enemyInReach, int distanceToMelee) =>
            !casting && !underAttack && !enemyInReach && distanceToMelee != int.MaxValue && distanceToMelee > CasterReach;

        public static string Label(TargetRole role) => role switch
        {
            TargetRole.Healer => "healer",
            TargetRole.Caster => "caster",
            TargetRole.Fighter => "fighter",
            _ => "pet"
        };
    }
}
