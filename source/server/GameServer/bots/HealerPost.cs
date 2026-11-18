using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Where a group's back-line support stands during a fight (issue #48). Play report (Jay, 2026-11-16): on
    /// passive healing duty a healer stayed anchored to its formation slot behind the leader, so as the leader turned
    /// and stepped in melee the healer ran around behind it for the whole fight. Now it takes a post: a spot out of
    /// reach of the enemies and within casting range of every member, and it holds that spot while it watches the
    /// group. It moves again only when the post stops being good (an enemy comes close, or a member gets too far).
    /// Pure, unit tested; the brain side is BotBrain.HealerPost.
    /// </summary>
    public static class HealerPost
    {
        /// <summary>
        /// The nearest an enemy may stand to a post (open ground / dungeon). Jay (v1.1.20, #56/#126): at 500 the healers
        /// stood so far off that they looked out of the fight; 350 keeps them just out of melee reach.
        /// </summary>
        public const int OpenStandoff = 350;
        public const int TightStandoff = 220;

        /// <summary>
        /// The farthest a group member may stand from a post (open ground / dungeon). Heals reach 1500-2000; this leaves
        /// room for the fight to drift before the healer has to move, and keeps it close enough for line of sight in a
        /// dungeon.
        /// </summary>
        public const int OpenReach = 750;
        public const int TightReach = 600;

        /// <summary>A post that has stopped being good is replaced no more often than this, so the healer does not dither.</summary>
        public const int RecheckMilliseconds = 1500;

        /// <summary>Close enough to the post to stand.</summary>
        public const int ArriveTolerance = 40;

        /// <summary>A held post is kept until an enemy is this share of the standoff away, or a member this share past reach.</summary>
        public const double KeepStandoffShare = 0.6;
        public const double KeepReachShare = 1.1;

        private const int Angles = 16;
        private const int Rings = 4;

        public static int Standoff(bool tight) => tight ? TightStandoff : OpenStandoff;

        public static int Reach(bool tight) => tight ? TightReach : OpenReach;

        /// <summary>No enemy within the standoff and every member within reach.</summary>
        public static bool IsGood(Vector2 point, IReadOnlyList<Vector2> members, IReadOnlyList<Vector2> enemies, bool tight) =>
            Fits(point, members, enemies, Standoff(tight), Reach(tight));

        /// <summary>Whether a post taken earlier is still worth holding (looser than <see cref="IsGood"/>, so it does not flicker).</summary>
        public static bool Keep(Vector2 post, IReadOnlyList<Vector2> members, IReadOnlyList<Vector2> enemies, bool tight) =>
            Fits(post, members, enemies, Standoff(tight) * KeepStandoffShare, Reach(tight) * KeepReachShare);

        /// <summary>
        /// The post for this fight: the healer's own spot when it is already good (it does not move at all), otherwise
        /// a good point on rings around the fight, as close to the group as the standoff allows and then as close to the
        /// healer as possible (#56: healers belong near the group, not off to the side). Null when no point works (the
        /// group is spread wider than one healer can cover); the caller then keeps its usual formation.
        /// </summary>
        public static Vector2? Pick(Vector2 self, IReadOnlyList<Vector2> members, IReadOnlyList<Vector2> enemies, bool tight)
        {
            members ??= [];
            enemies ??= [];
            if (members.Count == 0)
                return null;
            if (IsGood(self, members, enemies, tight))
                return self;

            Vector2 fight = Centroid(enemies.Count > 0 ? enemies : members);
            Vector2 group = Centroid(members);
            int standoff = Standoff(tight);
            int step = tight ? 60 : 120;
            Vector2? best = null;
            float bestDistance = float.MaxValue;
            for (int ring = 0; ring < Rings; ring++)
            {
                float radius = standoff + step / 2f + ring * step;
                for (int i = 0; i < Angles; i++)
                {
                    double angle = i * 2 * Math.PI / Angles;
                    Vector2 point = fight + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                    if (!IsGood(point, members, enemies, tight))
                        continue;
                    float distance = Vector2.Distance(group, point) + 0.5f * Vector2.Distance(self, point);
                    if (distance < bestDistance)
                    {
                        best = point;
                        bestDistance = distance;
                    }
                }
            }
            return best;
        }

        public static Vector2 Centroid(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count == 0)
                return Vector2.Zero;
            Vector2 sum = Vector2.Zero;
            foreach (Vector2 point in points)
                sum += point;
            return sum / points.Count;
        }

        private static bool Fits(Vector2 point, IReadOnlyList<Vector2> members, IReadOnlyList<Vector2> enemies, double standoff, double reach)
        {
            double standoffSquared = standoff * standoff, reachSquared = reach * reach;
            foreach (Vector2 enemy in enemies ?? [])
                if (Vector2.DistanceSquared(point, enemy) < standoffSquared)
                    return false;
            foreach (Vector2 member in members ?? [])
                if (Vector2.DistanceSquared(point, member) > reachSquared)
                    return false;
            return true;
        }
    }
}
