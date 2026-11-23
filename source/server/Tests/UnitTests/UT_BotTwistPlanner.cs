using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Issue #32: chant and song twisting that improves with the bot's level (BotTwistPlanner).</summary>
    [TestFixture]
    public class UT_BotTwistPlanner
    {
        private const int Anchor = 1, Second = 2, Third = 3, Heal = 4, Speed = 5;
        private const long Now = 100_000;

        private static readonly TwistNeeds Fight = new(true, false, true, 100, false, false);

        private static TwistSong S(int id, int priority, long remaining = 5_000, int cooldown = 0, int cast = 0,
            eSpellType type = eSpellType.DamageAdd) =>
            new(id, type, priority, cast, cooldown, remaining);

        /// <summary>A memory whose anchor has been held for a long time.</summary>
        private static TwistMemory Held(int anchor = Anchor) =>
            new() { AnchorId = anchor, AnchorSince = Now - 60_000, LastCastId = anchor, LastCastTick = Now - 60_000 };

        private static TwistDecision Plan(TwistTier tier, TwistMemory memory, int active, bool moving = false,
            TwistNeeds? needs = null, bool keep = false, bool solo = true, params TwistSong[] songs) =>
            BotTwistPlanner.Plan(tier, needs ?? Fight, songs, active, memory, Now, moving, keep, solo);

        [TestCase(1, TwistTier.Novice)]
        [TestCase(14, TwistTier.Novice)]
        [TestCase(15, TwistTier.Practiced)]
        [TestCase(29, TwistTier.Practiced)]
        [TestCase(30, TwistTier.Skilled)]
        [TestCase(39, TwistTier.Skilled)]
        [TestCase(40, TwistTier.Expert)]
        [TestCase(50, TwistTier.Expert)]
        public void TierComesFromLevel(int level, TwistTier tier) =>
            Assert.That(BotTwistPlanner.TierFor(level), Is.EqualTo(tier));

        [Test]
        public void TierSettingParsesAndFallsBackOnTypos()
        {
            Assert.That(BotTwistPlanner.ParseThree("1,1,1", BotTwistPlanner.DefaultTierLevels, 1, 100), Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(BotTwistPlanner.TierFor(5, new[] { 1, 1, 1 }), Is.EqualTo(TwistTier.Expert));
            Assert.That(BotTwistPlanner.TierFor(50, new[] { 99, 99, 99 }), Is.EqualTo(TwistTier.Novice));
            foreach (string bad in new[] { "", "15,30", "a,b,c", "0,30,40", "15,30,400" })
                Assert.That(BotTwistPlanner.ParseThree(bad, BotTwistPlanner.DefaultTierLevels, 1, 100),
                    Is.EqualTo(BotTwistPlanner.DefaultTierLevels), bad);
        }

        [Test]
        public void NoviceHoldsItsAnchorAndNeverTwists()
        {
            var d = Plan(TwistTier.Novice, Held(), Anchor, songs: new[] { S(Anchor, 90), S(Second, 50, remaining: 0) });
            Assert.That(d.CastId, Is.Zero);
            Assert.That(d.Why, Is.EqualTo("hold"));
        }

        [Test]
        public void PracticedTwistsOneSecondaryOnly()
        {
            TwistSong[] songs = { S(Anchor, 90), S(Second, 50, remaining: 5_000), S(Third, 40, remaining: 0) };
            Assert.That(Plan(TwistTier.Practiced, Held(), Anchor, songs: songs).CastId, Is.Zero,
                "the third song is beyond a Practiced bot's one secondary");
            Assert.That(Plan(TwistTier.Skilled, Held(), Anchor, songs: songs).CastId, Is.EqualTo(Third));
        }

        [Test]
        public void SecondariesGoByNeedNotByWhicheverRunsOutFirst()
        {
            // Expert: both secondaries are due; the one the group needs more goes first.
            TwistSong[] songs = { S(Anchor, 90), S(Second, 30, remaining: 0), S(Third, 70, remaining: 500) };
            Assert.That(Plan(TwistTier.Expert, Held(), Anchor, songs: songs).CastId, Is.EqualTo(Third));
        }

        [Test]
        public void PracticedRefreshesWithASaferMarginThanExpert()
        {
            // Child has 2.0 s left; round trip for instants is 1.0 s. Practiced adds 1.5 s, Expert none.
            TwistSong[] songs = { S(Anchor, 90), S(Second, 50, remaining: 2_000) };
            Assert.That(Plan(TwistTier.Practiced, Held(), Anchor, songs: songs).CastId, Is.EqualTo(Second));
            Assert.That(Plan(TwistTier.Expert, Held(), Anchor, songs: songs).CastId, Is.Zero);
        }

        [Test]
        public void OnlySkilledAndExpertTwistWhileMoving()
        {
            TwistSong[] songs = { S(Anchor, 90), S(Second, 50, remaining: 0) };
            Assert.That(Plan(TwistTier.Practiced, Held(), Anchor, moving: true, songs: songs).Why, Is.EqualTo("moving"));
            Assert.That(Plan(TwistTier.Skilled, Held(), Anchor, moving: true, songs: songs).CastId, Is.EqualTo(Second));
        }

        [Test]
        public void TwistGapShrinksWithTier()
        {
            TwistSong[] songs = { S(Anchor, 90), S(Second, 50, remaining: 0) };
            TwistMemory practiced = Held(); practiced.LastTwistTick = Now - 3_000;
            Assert.That(Plan(TwistTier.Practiced, practiced, Anchor, songs: songs).Why, Is.EqualTo("gap"));
            TwistMemory skilled = Held(); skilled.LastTwistTick = Now - 3_000;
            Assert.That(Plan(TwistTier.Skilled, skilled, Anchor, songs: songs).CastId, Is.EqualTo(Second));
            Assert.That(BotTwistPlanner.GapMilliseconds(TwistTier.Novice), Is.EqualTo(int.MaxValue));
            Assert.That(BotTwistPlanner.GapMilliseconds(TwistTier.Practiced, new[] { 9, 4, 1 }), Is.EqualTo(9_000));
        }

        [Test]
        public void ReturnsToTheAnchorAtOnceEvenWhileMovingOrHeld()
        {
            // A Paladin (keep while moving) just twisted: active is the secondary, it comes straight back.
            TwistMemory memory = Held();
            memory.LastCastId = Second; memory.LastCastTick = Now - 750; memory.AnchorSince = Now - 1_000;
            var d = Plan(TwistTier.Practiced, memory, Second, moving: true, keep: true,
                songs: new[] { S(Anchor, int.MaxValue), S(Second, 50, cooldown: 7_000) });
            Assert.That(d.CastId, Is.EqualTo(Anchor));
            Assert.That(d.Twist, Is.False);
            Assert.That(d.Why, Is.EqualTo("return"));
        }

        [Test]
        public void NeverLeavesTheAnchorWhileItsReuseRunsOrItsChildIsAboutToEnd()
        {
            Assert.That(Plan(TwistTier.Expert, Held(), Anchor,
                songs: new[] { S(Anchor, 90, cooldown: 4_000), S(Second, 50, remaining: 0) }).Why, Is.EqualTo("anchor-reuse"));
            Assert.That(Plan(TwistTier.Expert, Held(), Anchor,
                songs: new[] { S(Anchor, 90, remaining: 900), S(Second, 50, remaining: 0) }).CastId, Is.Zero);
            Assert.That(Plan(TwistTier.Expert, Held(), Anchor,
                songs: new[] { S(Anchor, 90), S(Second, 50, remaining: 0, cooldown: 2_000) }).CastId, Is.Zero);
        }

        [Test]
        public void NeverRecastsTheChantItJustCast()
        {
            // Casting a running chant turns it off: the pulse may not show yet, so the guard holds.
            var memory = new TwistMemory { AnchorId = Anchor, AnchorSince = Now - 1_000, LastCastId = Anchor, LastCastTick = Now - 1_000 };
            Assert.That(Plan(TwistTier.Expert, memory, 0, songs: new[] { S(Anchor, 90) }).Why, Is.EqualTo("recast-guard"));
            Assert.That(Plan(TwistTier.Expert, Held(), Anchor, songs: new[] { S(Anchor, 90) }).CastId, Is.Zero,
                "a running anchor is never cast again");
        }

        [Test]
        public void AnchorIsHeldByTierThenSwitches()
        {
            TwistSong[] songs = { S(Second, 95), S(Anchor, 60) };
            var recent = new TwistMemory { AnchorId = Anchor, AnchorSince = Now - 5_000, LastCastId = Anchor, LastCastTick = Now - 5_000 };
            Assert.That(Plan(TwistTier.Novice, recent, Anchor, songs: songs).CastId, Is.Zero, "Novice holds 10 s");
            var recent2 = new TwistMemory { AnchorId = Anchor, AnchorSince = Now - 5_000, LastCastId = Anchor, LastCastTick = Now - 5_000 };
            var d = Plan(TwistTier.Expert, recent2, Anchor, songs: songs);
            Assert.That((d.CastId, d.Why), Is.EqualTo((Second, "switch")), "Expert holds 4 s");
        }

        [Test]
        public void PaladinAnchorWaitsWhileMoving()
        {
            TwistSong[] songs = { S(Second, int.MaxValue), S(Anchor, 30) };
            Assert.That(Plan(TwistTier.Expert, Held(), Anchor, moving: true, keep: true, songs: songs).CastId, Is.Not.EqualTo(Second));
            Assert.That(Plan(TwistTier.Expert, Held(), Anchor, moving: false, keep: true, songs: songs).CastId, Is.EqualTo(Second));
        }

        [Test]
        public void SkilledAndExpertSwitchToTheHealAtOnceInAnEmergency()
        {
            TwistSong[] songs = { S(Heal, int.MaxValue, type: eSpellType.CombatHeal), S(Anchor, 75) };
            TwistMemory Fresh() => new() { AnchorId = Anchor, AnchorSince = Now - 500, LastCastId = Anchor, LastCastTick = Now - 500 };
            var hurt50 = new TwistNeeds(true, false, true, 50, false, false);
            var hurt35 = hurt50 with { LowestHealthPercent = 35 };
            Assert.That(Plan(TwistTier.Expert, Fresh(), Anchor, moving: true, needs: hurt50, keep: true, songs: songs).Why, Is.EqualTo("emergency"));
            Assert.That(Plan(TwistTier.Skilled, Fresh(), Anchor, needs: hurt50, keep: true, songs: songs).CastId, Is.Not.EqualTo(Heal),
                "Skilled reacts under 40%");
            Assert.That(Plan(TwistTier.Skilled, Fresh(), Anchor, needs: hurt35, keep: true, songs: songs).CastId, Is.EqualTo(Heal));
            Assert.That(Plan(TwistTier.Practiced, Fresh(), Anchor, needs: hurt35 with { LowestHealthPercent = 5 }, keep: true, songs: songs).CastId,
                Is.Not.EqualTo(Heal), "Practiced waits out its hold");
        }

        [Test]
        public void TravelStartSwitchesToSpeedWithoutWaitingForTheHold()
        {
            var traveling = new TwistNeeds(false, true, true, 100, false, false);
            var memory = new TwistMemory { AnchorId = Anchor, AnchorSince = Now - 1_000, LastCastId = Anchor, LastCastTick = Now - 1_000 };
            var d = Plan(TwistTier.Novice, memory, Anchor, needs: traveling,
                songs: new[] { S(Speed, BotTwistPlanner.Score(eSpellType.SpeedEnhancement, traveling), type: eSpellType.SpeedEnhancement),
                    S(Anchor, BotTwistPlanner.Score(eSpellType.HealthRegenBuff, traveling), type: eSpellType.HealthRegenBuff) });
            Assert.That((d.CastId, d.Why), Is.EqualTo((Speed, "travel")));
        }

        [Test]
        public void SoloPaladinNeverTwists()
        {
            var solo = new TwistNeeds(true, false, false, 100, false, false);
            Assert.That(Plan(TwistTier.Expert, Held(), Anchor, needs: solo, keep: true, solo: false,
                songs: new[] { S(Anchor, int.MaxValue), S(Second, 50, remaining: 0) }).Why, Is.EqualTo("solo-hold"));
        }

        [Test]
        public void AdoptsTheRunningSongAsItsAnchor()
        {
            var memory = new TwistMemory();
            var d = Plan(TwistTier.Novice, memory, Second, songs: new[] { S(Anchor, 90), S(Second, 50) });
            Assert.That(d.CastId, Is.EqualTo(Anchor), "a brain that never cast switches to its best song at once");
            var cast = new TwistMemory { LastCastId = Second, LastCastTick = Now - 2_000 };
            Assert.That(Plan(TwistTier.Novice, cast, Second, songs: new[] { S(Anchor, 90), S(Second, 50) }).CastId, Is.Zero,
                "a song it cast 2 s ago is held for the Novice 10 s");
        }

        [Test]
        public void CommittedRecordsOnlyCastsThatWentOut()
        {
            var memory = Held();
            var twist = new TwistDecision(Second, "twist", Anchor, true);
            BotTwistPlanner.Committed(memory, twist, Now);
            Assert.That((memory.LastCastId, memory.LastCastTick, memory.LastTwistTick), Is.EqualTo((Second, Now, Now)));
            BotTwistPlanner.Committed(memory, TwistDecision.None("steady", Anchor), Now + 1);
            Assert.That(memory.LastCastTick, Is.EqualTo(Now));
        }

        [Test]
        public void NeedsDecideTheSong()
        {
            var travel = new TwistNeeds(false, true, true, 100, false, false);
            Assert.That(BotTwistPlanner.Score(eSpellType.SpeedEnhancement, travel),
                Is.GreaterThan(BotTwistPlanner.Score(eSpellType.EnduranceRegenBuff, travel)));
            var hurtFight = new TwistNeeds(true, false, true, 45, false, false);
            Assert.That(BotTwistPlanner.Score(eSpellType.CombatHeal, hurtFight),
                Is.GreaterThan(BotTwistPlanner.Score(eSpellType.DamageAdd, hurtFight)));
            Assert.That(BotTwistPlanner.Score(eSpellType.DamageAdd, Fight),
                Is.GreaterThan(BotTwistPlanner.Score(eSpellType.SpecArmorFactorBuff, Fight)));
            var lowPower = new TwistNeeds(false, false, true, 100, true, false);
            Assert.That(BotTwistPlanner.Score(eSpellType.PowerRegenBuff, lowPower),
                Is.GreaterThan(BotTwistPlanner.Score(eSpellType.SpeedEnhancement, lowPower)));
            Assert.That(BotTwistPlanner.Score(eSpellType.SpeedEnhancement, Fight), Is.EqualTo(1), "never 0: scores only reorder");
        }
    }
}
