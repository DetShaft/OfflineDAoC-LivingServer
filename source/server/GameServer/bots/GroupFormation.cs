using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Where a grouped bot stands relative to its leader or a camp anchor.
    /// Tanks take the front, ranged stand behind that line, and casters and
    /// healers stand further back. The shape changes with what the group is
    /// doing, and peers in the same lane get distinct side offsets so they
    /// cannot occupy one point.
    /// </summary>
    public static class GroupFormation
    {
        public enum Situation
        {
            Moving,
            Idle,
            Pulling,
            Fighting,
        }

        public enum Lane
        {
            Front,
            Flank,
            Ranged,
            Back,
        }

        public readonly record struct Member(string Name, eCharacterClass Class);

        public readonly record struct Slot(int Forward, int Lateral);

        public static Situation Classify(bool inCombat, bool leaderMoving, bool pulling) =>
            inCombat ? Situation.Fighting :
            pulling ? Situation.Pulling :
            leaderMoving ? Situation.Moving :
            Situation.Idle;

        public static Lane LaneFor(eCharacterClass characterClass)
        {
            if (characterClass is eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger)
                return Lane.Ranged;
            // A paladin is a tank first, so the healing check below does not put one in the back line.
            if (BotPartyRoles.For(characterClass) == BotPartyRole.Tank)
                return Lane.Front;
            if (AutonomousBotVoices.IsCasterClass(characterClass) ||
                BotPartyRoles.IsHealingClass(characterClass) ||
                characterClass is eCharacterClass.Warlock or eCharacterClass.Bainshee or eCharacterClass.Heretic ||
                characterClass is eCharacterClass.Bard or eCharacterClass.Minstrel)
                return Lane.Back;
            return Lane.Flank;
        }

        public static Slot ForMember(IReadOnlyList<Member> party, string name, Situation situation, bool tight)
        {
            Member[] members = party?
                .Where(member => !string.IsNullOrWhiteSpace(member.Name))
                .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            Member self = members.FirstOrDefault(member =>
                string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(self.Name))
                return SlotFor(Lane.Flank, 0, 1, situation, tight);

            Lane lane = LaneFor(self.Class);
            Member[] mates = members.Where(member => LaneFor(member.Class) == lane).ToArray();
            int index = Array.FindIndex(mates, member =>
                string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                index = 0;
            return SlotFor(lane, index, Math.Max(1, mates.Length), situation, tight);
        }

        public static Slot SlotFor(Lane lane, int indexInLane, int laneCount, Situation situation, bool tight)
        {
            (int forward, int width) = Shape(lane, situation, tight);
            int count = Math.Max(1, laneCount);
            int index = Math.Clamp(indexInLane, 0, count - 1);
            return new Slot(forward, LateralOffset(index, count, width));
        }

        /// <summary>Same key, same facing, so an idle huddle does not spin when someone turns.</summary>
        public static double StableFacing(string key)
        {
            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(key ?? string.Empty) & int.MaxValue;
            return (hash % 360) * (Math.PI / 180d);
        }

        public static double HeadingRadians(ushort heading) => heading * Math.PI * 2d / 4096d;

        /// <summary>Positive forward follows the facing. Positive lateral is to its left.</summary>
        public static Vector3 Place(Vector3 center, double facingRadians, Slot slot)
        {
            double fx = Math.Cos(facingRadians);
            double fy = Math.Sin(facingRadians);
            return new Vector3(
                center.X + (float)(fx * slot.Forward - fy * slot.Lateral),
                center.Y + (float)(fy * slot.Forward + fx * slot.Lateral),
                center.Z);
        }

        /// <summary>
        /// A ground snap that lands on the shared center would pile the group
        /// onto one point. Keep the role offset in that case.
        /// </summary>
        public static Vector3 KeepSpread(Vector3 center, Vector3 desired, Vector3 projected)
        {
            if (Vector3.DistanceSquared(projected, center) < 24f * 24f &&
                Vector3.DistanceSquared(desired, center) > 48f * 48f)
                return desired;
            return projected;
        }

        public static int LateralOffset(int index, int count, int width)
        {
            if (count <= 1 || width <= 0)
                return 0;
            return (int)Math.Round(-width / 2d + width * (double)index / (count - 1));
        }

        private static (int Forward, int Width) Shape(Lane lane, Situation situation, bool tight)
        {
            (int forward, int width) = (situation, lane) switch
            {
                (Situation.Moving, Lane.Front) => (-90, 110),
                (Situation.Moving, Lane.Flank) => (-160, 150),
                (Situation.Moving, Lane.Ranged) => (-220, 130),
                (Situation.Moving, Lane.Back) => (-290, 170),

                (Situation.Idle, Lane.Front) => (130, 150),
                (Situation.Idle, Lane.Flank) => (40, 220),
                (Situation.Idle, Lane.Ranged) => (-110, 170),
                (Situation.Idle, Lane.Back) => (-200, 190),

                (Situation.Pulling, Lane.Front) => (200, 100),
                (Situation.Pulling, Lane.Flank) => (50, 170),
                (Situation.Pulling, Lane.Ranged) => (-50, 140),
                (Situation.Pulling, Lane.Back) => (-170, 160),

                (Situation.Fighting, Lane.Front) => (150, 130),
                (Situation.Fighting, Lane.Flank) => (30, 200),
                (Situation.Fighting, Lane.Ranged) => (-90, 160),
                (Situation.Fighting, Lane.Back) => (-190, 180),

                _ => (80, 80),
            };
            if (!tight)
                return (forward, width);

            int scaledForward = (int)Math.Round(forward * 0.42);
            if (scaledForward == 0 && forward != 0)
                scaledForward = forward > 0 ? 24 : -24;
            int scaledWidth = Math.Min(70, Math.Max(width <= 0 ? 0 : 36, (int)Math.Round(width * 0.35)));
            return (scaledForward, scaledWidth);
        }
    }

    /// <summary>
    /// A formed party that has reached its camp holds there and spreads around
    /// the camp coordinate. The coordinate is the anchor. No client prop is
    /// added. The hold does not start a sale; a service that is already due
    /// is handled by the existing task-break path and this hold steps aside.
    /// </summary>
    public static class CampAnchorPolicy
    {
        public const long HoldMilliseconds = 90_000;
        public const int PlannerRestMinutes = 2;

        public static long OpenUntil(long nowTick) => nowTick + HoldMilliseconds;

        public static bool IsOpen(long nowTick, long untilTick, string anchoredCampId, string campId) =>
            untilTick > nowTick && !string.IsNullOrEmpty(campId) &&
            string.Equals(anchoredCampId, campId, StringComparison.Ordinal);

        public static bool BlocksNewPull(long nowTick, long untilTick, string anchoredCampId, string campId, bool inCombat) =>
            !inCombat && IsOpen(nowTick, untilTick, anchoredCampId, campId);

        /// <summary>
        /// True for a group that just reached its camp, before the planner
        /// draws another grind. Callers must already have preferred a due
        /// vendor, bank, exchange, trainer or craft stop.
        /// </summary>
        public static bool PlannerHolds(int groupSize, bool inPlayerLedGroup, string currentCampId, int minutesAtCamp) =>
            !inPlayerLedGroup && groupSize >= 2 && !string.IsNullOrWhiteSpace(currentCampId) &&
            minutesAtCamp >= 0 && minutesAtCamp < PlannerRestMinutes;

        public static bool ShouldGather(int groupSize, bool inCombat, bool serviceDue, bool hasCamp, bool sameRegion) =>
            groupSize >= 2 && !inCombat && !serviceDue && hasCamp && sameRegion;
    }
}
