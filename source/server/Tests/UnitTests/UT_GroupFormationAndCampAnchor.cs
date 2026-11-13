using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public class UT_GroupFormationAndCampAnchor
{
    private static readonly GroupFormation.Member[] Party =
    [
        new("Aldric", eCharacterClass.Armsman),
        new("Brenna", eCharacterClass.Paladin),
        new("Cedric", eCharacterClass.Mercenary),
        new("Dara", eCharacterClass.Blademaster),
        new("Eira", eCharacterClass.Scout),
        new("Fian", eCharacterClass.Ranger),
        new("Gwen", eCharacterClass.Cleric),
        new("Hedda", eCharacterClass.Wizard),
    ];

    [Test]
    public void LanesPutTanksForwardRangedBehindThemAndCastersWithHealers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Armsman), Is.EqualTo(GroupFormation.Lane.Front));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Paladin), Is.EqualTo(GroupFormation.Lane.Front));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Sluaghbinder), Is.EqualTo(GroupFormation.Lane.Front));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Mercenary), Is.EqualTo(GroupFormation.Lane.Flank));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Skald), Is.EqualTo(GroupFormation.Lane.Flank));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Scout), Is.EqualTo(GroupFormation.Lane.Ranged));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Hunter), Is.EqualTo(GroupFormation.Lane.Ranged));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Ranger), Is.EqualTo(GroupFormation.Lane.Ranged));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Cleric), Is.EqualTo(GroupFormation.Lane.Back));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Druid), Is.EqualTo(GroupFormation.Lane.Back));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Wizard), Is.EqualTo(GroupFormation.Lane.Back));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Bard), Is.EqualTo(GroupFormation.Lane.Back));
            Assert.That(GroupFormation.LaneFor(eCharacterClass.Minstrel), Is.EqualTo(GroupFormation.Lane.Back));
        });
    }

    [Test]
    public void FightingOrderIsTankThenRangedThenHealer()
    {
        foreach (bool playerLed in new[] { false, true })
        foreach (GroupFormation.Situation situation in Enum.GetValues<GroupFormation.Situation>())
        {
            GroupFormation.Slot tank = GroupFormation.ForMember(Party, "Aldric", situation, false, playerLed);
            GroupFormation.Slot bow = GroupFormation.ForMember(Party, "Eira", situation, false, playerLed);
            GroupFormation.Slot healer = GroupFormation.ForMember(Party, "Gwen", situation, false, playerLed);
            Assert.That(tank.Forward, Is.GreaterThan(bow.Forward), $"{situation} playerLed={playerLed}");
            Assert.That(bow.Forward, Is.GreaterThan(healer.Forward), $"{situation} playerLed={playerLed}");
        }
    }

    [TestCase((ushort)0, 1000f, 1100f)]
    [TestCase((ushort)1024, 900f, 1000f)]
    [TestCase((ushort)2048, 1000f, 900f)]
    [TestCase((ushort)3072, 1100f, 1000f)]
    public void HeadingPutsTheForwardSlotWhereTheGameLooks(ushort heading, float expectedX, float expectedY)
    {
        Vector3 leader = new(1000, 1000, 0);
        double facing = GroupFormation.FacingFromHeading(heading);
        Vector3 ahead = GroupFormation.Place(leader, facing, new GroupFormation.Slot(100, 0));
        Vector3 behind = GroupFormation.Place(leader, facing, new GroupFormation.Slot(-100, 0));
        Point2D gameAhead = new Point2D(1000, 1000).GetPointFromHeading(heading, 100);

        Assert.Multiple(() =>
        {
            Assert.That(ahead.X, Is.EqualTo(expectedX).Within(0.01f));
            Assert.That(ahead.Y, Is.EqualTo(expectedY).Within(0.01f));
            Assert.That(ahead.X, Is.EqualTo((float)gameAhead.X).Within(1f));
            Assert.That(ahead.Y, Is.EqualTo((float)gameAhead.Y).Within(1f));
            Assert.That(behind.X, Is.EqualTo(2000f - expectedX).Within(0.01f));
            Assert.That(behind.Y, Is.EqualTo(2000f - expectedY).Within(0.01f));
            ushort back = new Point2D(1000, 1000).GetHeading((int)Math.Round(ahead.X), (int)Math.Round(ahead.Y));
            Assert.That(Turn(back, heading), Is.LessThanOrEqualTo(1));
        });
    }

    [Test]
    public void PlayerHelpersStandBesideAndBehindThePlayerWhileIdle()
    {
        Vector3 player = new(5000, 5000, 0);
        foreach (bool tight in new[] { false, true })
        foreach (GroupFormation.Member member in Party)
        {
            GroupFormation.Slot slot = GroupFormation.ForMember(Party, member.Name, GroupFormation.Situation.Idle, tight,
                playerLed: true);
            Assert.That(slot.Forward, Is.LessThan(0), $"{member.Name} tight={tight}");

            foreach (ushort held in new ushort[] { 0, 1024, 3000 })
            foreach (int turn in new[] { -GroupFormation.PlayerIdleHeadingTolerance, 0, GroupFormation.PlayerIdleHeadingTolerance })
            {
                ushort looking = (ushort)((held + turn + 4096) % 4096);
                Assert.That(GroupFormation.HoldHeading(held, looking, GroupFormation.PlayerIdleHeadingTolerance),
                    Is.EqualTo(held));
                Vector3 spot = GroupFormation.Place(player, GroupFormation.FacingFromHeading(held), slot);
                Point2D sight = new Point2D(5000, 5000).GetPointFromHeading(looking, 100);
                Vector2 toSpot = Vector2.Normalize(new Vector2(spot.X - player.X, spot.Y - player.Y));
                Vector2 toSight = Vector2.Normalize(new Vector2(sight.X - player.X, sight.Y - player.Y));
                double degrees = Math.Acos(Math.Clamp(Vector2.Dot(toSpot, toSight), -1f, 1f)) * 180d / Math.PI;
                Assert.That(degrees, Is.GreaterThanOrEqualTo(60d),
                    $"{member.Name} tight={tight} held={held} looking={looking}");
            }
        }

        GroupFormation.Slot botLedTank = GroupFormation.ForMember(Party, "Aldric", GroupFormation.Situation.Idle, false);
        Assert.That(botLedTank.Forward, Is.GreaterThan(0), "a bot-led camp huddle still puts its tank forward");
    }

    [Test]
    public void IdleHeadingHoldsThroughSmallTurnsAndAcrossNorth()
    {
        const int tolerance = GroupFormation.PlayerIdleHeadingTolerance;
        Assert.Multiple(() =>
        {
            Assert.That(GroupFormation.HoldHeading(1000, 1300, tolerance), Is.EqualTo((ushort)1000));
            Assert.That(GroupFormation.HoldHeading(1000, 1600, tolerance), Is.EqualTo((ushort)1600));
            Assert.That(GroupFormation.HoldHeading(4000, 100, tolerance), Is.EqualTo((ushort)4000));
            Assert.That(GroupFormation.HoldHeading(4000, 700, tolerance), Is.EqualTo((ushort)700));
            Assert.That(GroupFormation.HoldHeading(100, 4000, tolerance), Is.EqualTo((ushort)100));
        });
    }

    [Test]
    public void SituationChangesTheSameBotsSlot()
    {
        GroupFormation.Slot moving = GroupFormation.ForMember(Party, "Aldric", GroupFormation.Situation.Moving, false);
        GroupFormation.Slot idle = GroupFormation.ForMember(Party, "Aldric", GroupFormation.Situation.Idle, false);
        GroupFormation.Slot pulling = GroupFormation.ForMember(Party, "Aldric", GroupFormation.Situation.Pulling, false);
        GroupFormation.Slot fighting = GroupFormation.ForMember(Party, "Aldric", GroupFormation.Situation.Fighting, false);

        Assert.Multiple(() =>
        {
            Assert.That(moving.Forward, Is.LessThan(0), "a moving tank stays behind the leader");
            Assert.That(idle.Forward, Is.GreaterThan(0));
            Assert.That(pulling.Forward, Is.GreaterThan(fighting.Forward));
            Assert.That(fighting.Forward, Is.GreaterThan(0));
            Assert.That(new[] { moving, idle, pulling, fighting }.Select(slot => slot.Forward).Distinct().Count(),
                Is.EqualTo(4));
        });
    }

    [Test]
    public void IdlePeersDoNotShareAPoint()
    {
        GroupFormation.Member[] three =
        [
            new("Ana", eCharacterClass.Mercenary),
            new("Bea", eCharacterClass.Mercenary),
            new("Cara", eCharacterClass.Mercenary),
        ];
        GroupFormation.Slot[] slots = three
            .Select(member => GroupFormation.ForMember(three, member.Name, GroupFormation.Situation.Idle, false))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(Apart(slots[0], slots[1]), Is.GreaterThanOrEqualTo(100));
            Assert.That(Apart(slots[1], slots[2]), Is.GreaterThanOrEqualTo(100));
            Assert.That(Apart(slots[0], slots[2]), Is.GreaterThanOrEqualTo(100));
            Assert.That(slots.Select(slot => (slot.Forward, slot.Lateral)).Distinct().Count(), Is.EqualTo(3));
        });
    }

    [Test]
    public void OutdoorPartyStaysSpreadAndInsideGroupCohesion()
    {
        foreach (bool playerLed in new[] { false, true })
        foreach (GroupFormation.Situation situation in Enum.GetValues<GroupFormation.Situation>())
        {
            GroupFormation.Slot[] slots = Party
                .Select(member => GroupFormation.ForMember(Party, member.Name, situation, false, playerLed))
                .ToArray();
            for (int i = 0; i < slots.Length; i++)
            {
                Assert.That(MathF.Sqrt(slots[i].Forward * slots[i].Forward + slots[i].Lateral * slots[i].Lateral),
                    Is.LessThan(360), $"{situation} playerLed={playerLed} {Party[i].Name} stays near the anchor");
                for (int j = i + 1; j < slots.Length; j++)
                    Assert.That(Apart(slots[i], slots[j]), Is.GreaterThanOrEqualTo(60),
                        $"{situation} playerLed={playerLed} {Party[i].Name} and {Party[j].Name}");
            }
        }
    }

    [Test]
    public void DungeonSlotsStayCloserThanTheOutdoorOnes()
    {
        GroupFormation.Slot outside = GroupFormation.ForMember(Party, "Gwen", GroupFormation.Situation.Fighting, false);
        GroupFormation.Slot inside = GroupFormation.ForMember(Party, "Gwen", GroupFormation.Situation.Fighting, true);
        Assert.That(Math.Abs(inside.Forward), Is.LessThan(Math.Abs(outside.Forward)));
        Assert.That(Math.Abs(inside.Lateral), Is.LessThanOrEqualTo(Math.Abs(outside.Lateral)));
    }

    [Test]
    public void TheSamePartyKeepsTheSameSlots()
    {
        GroupFormation.Slot first = GroupFormation.ForMember(Party, "Hedda", GroupFormation.Situation.Idle, false);
        GroupFormation.Slot second = GroupFormation.ForMember(Party, "Hedda", GroupFormation.Situation.Idle, false);
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void PlaceUsesFacingAndKeepSpreadRefusesTheSharedCenter()
    {
        Vector3 placed = GroupFormation.Place(new Vector3(1000, 2000, 30), 0, new GroupFormation.Slot(10, 4));
        Vector3 center = new(0, 0, 0);
        Vector3 desired = new(120, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(placed.X, Is.EqualTo(1010f).Within(0.01f));
            Assert.That(placed.Y, Is.EqualTo(2004f).Within(0.01f));
            Assert.That(placed.Z, Is.EqualTo(30f).Within(0.01f));
            Assert.That(GroupFormation.KeepSpread(center, desired, center), Is.EqualTo(desired));
            Assert.That(GroupFormation.KeepSpread(center, desired, new Vector3(90, 5, 0)),
                Is.EqualTo(new Vector3(90, 5, 0)));
            Assert.That(GroupFormation.Classify(true, true, true), Is.EqualTo(GroupFormation.Situation.Fighting));
            Assert.That(GroupFormation.Classify(false, true, true), Is.EqualTo(GroupFormation.Situation.Pulling));
            Assert.That(GroupFormation.Classify(false, true, false), Is.EqualTo(GroupFormation.Situation.Moving));
            Assert.That(GroupFormation.Classify(false, false, false), Is.EqualTo(GroupFormation.Situation.Idle));
        });
    }

    [Test]
    public void CampRestBlocksTheNextPullAndStepsAsideForAFightOrADueService()
    {
        long now = 10_000;
        long until = CampAnchorPolicy.OpenUntil(now);
        Assert.Multiple(() =>
        {
            Assert.That(until - now, Is.EqualTo(CampAnchorPolicy.HoldMilliseconds));
            Assert.That(CampAnchorPolicy.BlocksNewPull(now, until, "boar", "boar", inCombat: false), Is.True);
            Assert.That(CampAnchorPolicy.BlocksNewPull(now, until, "boar", "boar", inCombat: true), Is.False);
            Assert.That(CampAnchorPolicy.BlocksNewPull(until, until, "boar", "boar", inCombat: false), Is.False);
            Assert.That(CampAnchorPolicy.BlocksNewPull(now, until, "boar", "wolf", inCombat: false), Is.False);
            Assert.That(CampAnchorPolicy.ShouldGather(3, inCombat: false, serviceDue: false, hasCamp: true, sameRegion: true),
                Is.True);
            Assert.That(CampAnchorPolicy.ShouldGather(1, false, false, true, true), Is.False);
            Assert.That(CampAnchorPolicy.ShouldGather(3, false, serviceDue: true, true, true), Is.False);
            Assert.That(CampAnchorPolicy.ShouldGather(3, inCombat: true, false, true, true), Is.False);
            Assert.That(CampAnchorPolicy.ShouldGather(3, false, false, hasCamp: false, true), Is.False);
        });
    }

    [Test]
    public void PlannerRestsAGroupThatJustReachedCampAndStillSellsWhenTheBagIsFull()
    {
        AutonomousBotDecisionEngine.Camp[] camps = [Camp("boar")];
        var arrived = State() with { GroupSize = 3, CurrentCampId = "boar", MinutesAtCurrentCamp = 0 };
        var rested = arrived with { MinutesAtCurrentCamp = CampAnchorPolicy.PlannerRestMinutes };
        var solo = arrived with { GroupSize = 1 };
        var hurt = arrived with { HealthPercent = 40 };
        var led = arrived with { InPlayerLedGroup = true };
        var selling = arrived with { InventoryUsedPercent = 95 };
        var services = new[]
        {
            new AutonomousBotDecisionEngine.Service(eWorldServiceKind.Vendor, "Merchant", eRealm.Albion, 1, true, 0),
        };

        AutonomousBotDecisionEngine.Decision hold = AutonomousBotDecisionEngine.Choose(
            arrived, camps, [], new FixedRandom(0.01));
        AutonomousBotDecisionEngine.Decision grind = AutonomousBotDecisionEngine.Choose(
            rested, camps, [], new FixedRandom(0.01));
        AutonomousBotDecisionEngine.Decision alone = AutonomousBotDecisionEngine.Choose(
            solo, camps, [], new FixedRandom(0.5));
        AutonomousBotDecisionEngine.Decision injured = AutonomousBotDecisionEngine.Choose(
            hurt, camps, [], new FixedRandom(0.5));
        AutonomousBotDecisionEngine.Decision player = AutonomousBotDecisionEngine.Choose(
            led, camps, [], new FixedRandom(0.01));
        AutonomousBotDecisionEngine.Decision vendor = AutonomousBotDecisionEngine.Choose(
            selling, camps, services, new FixedRandom(0.01));

        Assert.Multiple(() =>
        {
            Assert.That(hold.Activity, Is.EqualTo(eAutonomousActivity.Rest));
            Assert.That(hold.TargetId, Is.EqualTo("boar"));
            Assert.That(grind.Activity, Is.EqualTo(eAutonomousActivity.GroupGrind));
            Assert.That(grind.TargetId, Is.EqualTo("boar"));
            Assert.That(alone.Activity, Is.EqualTo(eAutonomousActivity.Grind));
            Assert.That(injured.Activity, Is.EqualTo(eAutonomousActivity.Rest));
            Assert.That(injured.TargetId, Is.Empty);
            Assert.That(player.Activity, Is.EqualTo(eAutonomousActivity.Travel));
            Assert.That(vendor.Activity, Is.EqualTo(eAutonomousActivity.Vendor));
        });
    }

    private static int Turn(ushort a, ushort b) => Math.Abs(((a - b) % 4096 + 6144) % 4096 - 2048);

    private static float Apart(GroupFormation.Slot a, GroupFormation.Slot b)
    {
        float forward = a.Forward - b.Forward;
        float lateral = a.Lateral - b.Lateral;
        return MathF.Sqrt(forward * forward + lateral * lateral);
    }

    private static AutonomousBotDecisionEngine.State State() =>
        new(eRealm.Albion, 20, 100, 100, 100, 20, 0, 0, 0, 0, 5, string.Empty, 1, false, false, false);

    private static AutonomousBotDecisionEngine.Camp Camp(string id) =>
        new(id, "Test Zone", "boar", eRealm.Albion, 1, ConColor.BLUE, ConColor.BLUE, true, false, false, 20, 0, 1);

    private sealed class FixedRandom(double sample) : Random
    {
        protected override double Sample() => sample;
    }
}
