using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// 1.1.14, friends come to you (BotFriendTravel): a bot friend invited from too far away sets out, says so, and joins on arrival.
/// These are the pure rules; the move and the group join use the same calls as a released groupmate joining its leader. Tests
/// only: nothing here was seen on the owner's PC yet.
/// </summary>
[TestFixture]
public class UT_BotFriendTravel
{
    [Test]
    public void OnlyAnAutonomousFriendTooFarAwayTravels()
    {
        Assert.That(BotFriendTravel.Travels(autonomous: true, helper: false, friend: true, closeEnough: false), Is.True);
        // Close enough: the ordinary invite, it walks over.
        Assert.That(BotFriendTravel.Travels(true, false, true, true), Is.False);
        // Not a friend: still "meet up first".
        Assert.That(BotFriendTravel.Travels(true, false, false, false), Is.False);
        // A companion bot or a temporary helper is never sent across the realm by this.
        Assert.That(BotFriendTravel.Travels(false, false, true, false), Is.False);
        Assert.That(BotFriendTravel.Travels(true, true, true, false), Is.False);
    }

    [Test]
    public void TheJourneyIsThirtySecondsAndThirtyMorePerZoneAway()
    {
        // Jay, 2026-11-11: "30 seconds if they are in my region, and then 30 seconds extra per zone they are away from me".
        Assert.That(BotFriendTravel.TravelDelayMs(0), Is.EqualTo(30_000));
        Assert.That(BotFriendTravel.TravelDelayMs(1), Is.EqualTo(60_000));
        Assert.That(BotFriendTravel.TravelDelayMs(4), Is.EqualTo(150_000));
        Assert.That(BotFriendTravel.TravelDelayMs(-1), Is.EqualTo(30_000));
        // However far: at most five minutes.
        Assert.That(BotFriendTravel.TravelDelayMs(50), Is.EqualTo(BotFriendTravel.MaxDelayMs));
        Assert.That(BotFriendTravel.TravelDelayMs(BotFriendTravel.OtherRegionZones), Is.EqualTo(120_000));
    }

    [Test]
    public void ZonesAreCountedOnTheRegionsGrid()
    {
        const int Z = BotFriendTravel.ZoneSize;
        Assert.That(BotFriendTravel.ZonesBetween(0, 0, Z, Z, 0, 0, Z, Z), Is.EqualTo(0));
        // Neighbours east and south.
        Assert.That(BotFriendTravel.ZonesBetween(0, 0, Z, Z, Z, 0, Z, Z), Is.EqualTo(1));
        Assert.That(BotFriendTravel.ZonesBetween(0, Z, Z, Z, 0, 0, Z, Z), Is.EqualTo(1));
        // A diagonal neighbour crosses two borders.
        Assert.That(BotFriendTravel.ZonesBetween(0, 0, Z, Z, Z, Z, Z, Z), Is.EqualTo(2));
        // Three zones east, one zone between.
        Assert.That(BotFriendTravel.ZonesBetween(0, 0, Z, Z, 3 * Z, 0, Z, Z), Is.EqualTo(3));
        // A double-width zone beside a normal one is still one zone away.
        Assert.That(BotFriendTravel.ZonesBetween(0, 0, 2 * Z, Z, 2 * Z, 0, Z, Z), Is.EqualTo(1));
        // Order doesn't matter.
        Assert.That(BotFriendTravel.ZonesBetween(3 * Z, 2 * Z, Z, Z, 0, 0, Z, Z), Is.EqualTo(BotFriendTravel.ZonesBetween(0, 0, Z, Z, 3 * Z, 2 * Z, Z, Z)));
    }

    [Test]
    public void AFriendInAGroupTurnsDownTheFirstInviteAndTakesALaterOne()
    {
        long now = 1_000_000;
        // Not in a group of its own: it joins at once.
        Assert.That(BotFriendTravel.Regroup(false, null, now), Is.EqualTo(BotFriendTravel.RegroupAnswer.Join));
        // In a group: the first invite is turned down.
        Assert.That(BotFriendTravel.Regroup(true, null, now), Is.EqualTo(BotFriendTravel.RegroupAnswer.DeclineFirst));
        // Asked again too soon: still wrapping up.
        Assert.That(BotFriendTravel.Regroup(true, now, now + 10_000), Is.EqualTo(BotFriendTravel.RegroupAnswer.StillWrappingUp));
        // Asked again after the wait: it joins.
        Assert.That(BotFriendTravel.Regroup(true, now, now + BotFriendTravel.RegroupWaitMs), Is.EqualTo(BotFriendTravel.RegroupAnswer.Join));
        // Long after, the old answer is forgotten: a first invite again.
        Assert.That(BotFriendTravel.Regroup(true, now, now + BotFriendTravel.RegroupMemoryMs), Is.EqualTo(BotFriendTravel.RegroupAnswer.DeclineFirst));
        Assert.That(BotFriendTravel.InAGroupMessage("Haralhelm", 60),
            Is.EqualTo("Haralhelm is in a group right now and declines. Ask again in about 60 seconds."));
        for (int seed = 0; seed < 20; seed++)
        {
            Assert.That(BotFriendTravel.InAGroupLine(new Random(seed)), Does.Contain("again").Or.Contain("min"));
            Assert.That(BotFriendTravel.LeavingGroupLine(new Random(seed)), Is.Not.Empty);
        }
    }

    [Test]
    public void LongWaitsReadInMinutes()
    {
        Assert.That(BotFriendTravel.Duration(30), Is.EqualTo("30 seconds"));
        Assert.That(BotFriendTravel.Duration(1), Is.EqualTo("1 second"));
        Assert.That(BotFriendTravel.Duration(90), Is.EqualTo("1 minute 30 seconds"));
        Assert.That(BotFriendTravel.Duration(120), Is.EqualTo("2 minutes"));
        Assert.That(BotFriendTravel.OnTheWayMessage("Haralhelm", 150), Does.Contain("about 2 minutes 30 seconds"));
    }

    [Test]
    public void SecondsRoundUpAndNeverReadZero()
    {
        Assert.That(BotFriendTravel.Seconds(30_000), Is.EqualTo(30));
        Assert.That(BotFriendTravel.Seconds(29_001), Is.EqualTo(30));
        Assert.That(BotFriendTravel.Seconds(0), Is.EqualTo(1));
        Assert.That(BotFriendTravel.Seconds(-5), Is.EqualTo(1));
    }

    [Test]
    public void ATripHoldsTheBotUntilItGoesStale()
    {
        Assert.That(BotFriendTravel.StillUnderway(1_000, 1_000 + BotFriendTravel.MaxDelayMs), Is.True);
        Assert.That(BotFriendTravel.StillUnderway(1_000, 1_000 + BotFriendTravel.StaleTripMs), Is.False);
        // A trip outlives its longest honest end: the journey plus every wait for a fight.
        Assert.That(BotFriendTravel.StaleTripMs,
            Is.GreaterThan(BotFriendTravel.MaxDelayMs + BotFriendTravel.CombatWaits * BotFriendTravel.CombatWaitMs));
    }

    [Test]
    public void ThePlayerIsToldTheFriendIsOnTheWayAndWhenItArrives()
    {
        Assert.That(BotFriendTravel.OnTheWayMessage("Haralhelm", 30),
            Is.EqualTo("Haralhelm is on the way to you and will join your group on arrival (about 30 seconds)."));
        Assert.That(BotFriendTravel.ArrivedMessage("Haralhelm"), Is.EqualTo("Haralhelm has arrived and joins your group."));
        Assert.That(BotFriendTravel.AlreadyOnTheWayMessage("Haralhelm", 12), Does.Contain("12 seconds left"));
        Assert.That(BotFriendTravel.AlreadyOnTheWayMessage("Haralhelm", 0), Does.Contain("any moment"));
        Assert.That(BotFriendTravel.CalledOffMessage("Haralhelm", "they got pulled into a siege"),
            Is.EqualTo("Haralhelm could not reach you: they got pulled into a siege."));
    }

    [Test]
    public void TheFriendsTellNamesWhereItComesFromOnlyFromAnotherZone()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            string near = BotFriendTravel.OnMyWayLine("Avalon Marsh", sameZone: true, new Random(seed));
            Assert.That(near, Is.Not.Empty);
            Assert.That(near.ToLowerInvariant(), Does.Not.Contain("avalon"), near);

            string far = BotFriendTravel.OnMyWayLine("Avalon Marsh", sameZone: false, new Random(seed));
            Assert.That(far, Does.Contain("omw").Or.Contain("way").Or.Contain("heading"), far);

            Assert.That(BotFriendTravel.OnMyWayLine(null, false, new Random(seed)), Is.Not.Empty);
            Assert.That(BotFriendTravel.ArrivalLine(new Random(seed)), Is.Not.Empty);
        }
        string[] lines = Enumerable.Range(0, 40).Select(seed => BotFriendTravel.OnMyWayLine("Avalon Marsh", true, new Random(seed))).Distinct().ToArray();
        Assert.That(lines.Length, Is.GreaterThan(1), "the friend doesn't always say the same thing");
    }
}
