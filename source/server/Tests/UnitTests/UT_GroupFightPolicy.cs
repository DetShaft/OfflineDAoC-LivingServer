using DOL.GS;
using NUnit.Framework;
using static DOL.GS.GroupFightPolicy;

namespace DOL.UnitTests
{
    /// <summary>
    /// #165: the player group's fight mode. Target priority follows docs/PVP-BEHAVIOR-RESEARCH.md section E and
    /// Table 4, the same in every realm.
    /// </summary>
    [TestFixture]
    public class UT_GroupFightPolicy
    {
        private static Candidate C(TargetRole role, int hp = 100, int distance = 500, bool mezzed = false, bool backLine = false) =>
            new(role, hp, distance, mezzed, backLine);

        [Test]
        public void HealersThenCastersThenFightersThenPets()
        {
            Candidate[] list = { C(TargetRole.Pet), C(TargetRole.Fighter), C(TargetRole.Caster), C(TargetRole.Healer) };
            Assert.That(PickFocus(list), Is.EqualTo(3));
            list[3] = C(TargetRole.Fighter, hp: 5);
            Assert.That(PickFocus(list), Is.EqualTo(2), "A caster outranks a fighter at 5% health.");
        }

        [Test]
        public void LowestHealthThenClosestWithinARole()
        {
            Candidate[] list = { C(TargetRole.Caster, hp: 80, distance: 200), C(TargetRole.Caster, hp: 40, distance: 1400) };
            Assert.That(PickFocus(list), Is.EqualTo(1));
            list[1] = C(TargetRole.Caster, hp: 80, distance: 100);
            Assert.That(PickFocus(list), Is.EqualTo(1));
        }

        [Test]
        public void NeverFocusesAMezzedEnemy()
        {
            Candidate[] list = { C(TargetRole.Healer, mezzed: true), C(TargetRole.Fighter) };
            Assert.That(PickFocus(list), Is.EqualTo(1));
            Assert.That(PickFocus(new[] { C(TargetRole.Healer, mezzed: true) }), Is.EqualTo(-1));
            Assert.That(PickFocus(new Candidate[0]), Is.EqualTo(-1));
        }

        [Test]
        public void TankPeelsTheEnemyOnTheBackLine()
        {
            Candidate[] list =
            {
                C(TargetRole.Healer),
                C(TargetRole.Fighter, distance: 900, backLine: true),
                C(TargetRole.Fighter, distance: 300, backLine: true),
                C(TargetRole.Fighter, distance: 100, backLine: true, mezzed: true)
            };
            Assert.That(PickPeel(list), Is.EqualTo(2));
            Assert.That(PickPeel(new[] { C(TargetRole.Healer) }), Is.EqualTo(-1), "Nobody on the back line: no peel.");
        }

        [Test]
        public void RolesComeFromClassData()
        {
            Assert.That(RoleOf(eCharacterClass.Cleric, false), Is.EqualTo(TargetRole.Healer));
            Assert.That(RoleOf(eCharacterClass.Healer, false), Is.EqualTo(TargetRole.Healer));
            Assert.That(RoleOf(eCharacterClass.Druid, false), Is.EqualTo(TargetRole.Healer));
            Assert.That(RoleOf(eCharacterClass.Shaman, false), Is.EqualTo(TargetRole.Healer));
            Assert.That(RoleOf(eCharacterClass.Paladin, false), Is.EqualTo(TargetRole.Fighter), "A Paladin is a tank first.");
            Assert.That(RoleOf(eCharacterClass.Wizard, true), Is.EqualTo(TargetRole.Caster));
            Assert.That(RoleOf(eCharacterClass.Runemaster, true), Is.EqualTo(TargetRole.Caster));
            Assert.That(RoleOf(eCharacterClass.Eldritch, true), Is.EqualTo(TargetRole.Caster));
            Assert.That(RoleOf(eCharacterClass.Berserker, false), Is.EqualTo(TargetRole.Fighter));
            Assert.That(RoleOf(null, false), Is.EqualTo(TargetRole.Pet));
        }

        [Test]
        public void ExitsAfterSixQuietSeconds()
        {
            Assert.That(ShouldExit(10_000, 10_000 - ExitDelayMilliseconds + 1), Is.False);
            Assert.That(ShouldExit(10_000, 10_000 - ExitDelayMilliseconds), Is.True);
        }

        [Test]
        public void LeashDropsFarTargetsUnlessTheFightCameToTheGroup()
        {
            Assert.That(BeyondLeash(1600, 1500, leaderTarget: false, attackingGroupNearLeader: false), Is.True);
            Assert.That(BeyondLeash(1500, 1500, false, false), Is.False);
            Assert.That(BeyondLeash(2500, 1500, leaderTarget: true, attackingGroupNearLeader: false), Is.False,
                "The player's own target is never dropped.");
            Assert.That(BeyondLeash(2500, 1500, false, attackingGroupNearLeader: true), Is.False,
                "An enemy hitting the bot or a groupmate near the player is fought.");
        }

        [Test]
        public void ChaseDistanceSettingIsClamped()
        {
            Assert.That(ChaseDistance(1500), Is.EqualTo(1500));
            Assert.That(ChaseDistance(0), Is.EqualTo(DefaultChaseDistance));
            Assert.That(ChaseDistance(100), Is.EqualTo(MinChaseDistance));
            Assert.That(ChaseDistance(99_999), Is.EqualTo(MaxChaseDistance));
        }

        [Test]
        public void LeaderTargetFirstUnlessMezzed()
        {
            Assert.That(FollowsLeaderTarget(true, false), Is.True);
            Assert.That(FollowsLeaderTarget(true, true), Is.False, "A groupmate's mez must hold.");
            Assert.That(FollowsLeaderTarget(false, false), Is.False);
        }

        [Test]
        public void BetterRoleReplacesAStickyFocus()
        {
            // #170: the first focus was the enemy melee that charged in; the healer behind it must take over.
            Assert.That(ShouldReplaceFocus(C(TargetRole.Fighter, hp: 20), C(TargetRole.Healer)), Is.True);
            Assert.That(ShouldReplaceFocus(C(TargetRole.Caster), C(TargetRole.Healer)), Is.True);
            Assert.That(ShouldReplaceFocus(C(TargetRole.Caster, hp: 90), C(TargetRole.Caster, hp: 10)), Is.False,
                "Never flip between equals.");
            Assert.That(ShouldReplaceFocus(C(TargetRole.Healer), C(TargetRole.Caster)), Is.False);
            Assert.That(ShouldReplaceFocus(C(TargetRole.Healer, mezzed: true), C(TargetRole.Fighter)), Is.True);
        }

        [Test]
        public void BusyBotsSwitchOnlyUpward()
        {
            Assert.That(ShouldSwitchToFocus(TargetRole.Fighter, TargetRole.Healer), Is.True);
            Assert.That(ShouldSwitchToFocus(TargetRole.Pet, TargetRole.Fighter), Is.True);
            Assert.That(ShouldSwitchToFocus(TargetRole.Caster, TargetRole.Caster), Is.False);
            Assert.That(ShouldSwitchToFocus(TargetRole.Healer, TargetRole.Caster), Is.False);
        }

        [Test]
        public void AnchorIsTheLivingGroupmateNearestTheBody()
        {
            Assert.That(AnchorIndex(new[] { true, true, true }, new[] { 900, 120, 400 }), Is.EqualTo(1));
            Assert.That(AnchorIndex(new[] { true, false, true }, new[] { 900, 120, 400 }), Is.EqualTo(2), "Dead groupmates are skipped.");
            Assert.That(AnchorIndex(new[] { false, false }, new[] { 1, 2 }), Is.EqualTo(-1));
        }

        [Test]
        public void CastTimeCrowdControlWaitsWhileUnderFire()
        {
            Assert.That(BotPvpCrowdControlPolicy.CastSurvives(2_500, underFire: true), Is.False);
            Assert.That(BotPvpCrowdControlPolicy.CastSurvives(2_500, underFire: false), Is.True);
            Assert.That(BotPvpCrowdControlPolicy.CastSurvives(0, underFire: true), Is.True, "Instant CC always may.");
        }

        [Test]
        public void ThreatsThatLandBeforeTheCastEnds()
        {
            // targetsCaster, mezzed, casting, rangedAttacker, distance, meleeReach, castTime
            Assert.That(BotPvpCrowdControlPolicy.Threatens(true, false, true, false, 1400, 250, 2_500), Is.True, "Already casting at it.");
            Assert.That(BotPvpCrowdControlPolicy.Threatens(true, false, false, true, 1400, 250, 2_500), Is.True,
                "A caster targeting it can start a nuke mid-cast.");
            Assert.That(BotPvpCrowdControlPolicy.Threatens(true, false, false, true, 1700, 250, 2_500), Is.False, "Out of spell range.");
            Assert.That(BotPvpCrowdControlPolicy.Threatens(true, false, false, false, 800, 250, 2_500), Is.True,
                "A melee enemy 800 away closes 625 units in 2.5 s.");
            Assert.That(BotPvpCrowdControlPolicy.Threatens(true, false, false, false, 1000, 250, 2_500), Is.False);
            Assert.That(BotPvpCrowdControlPolicy.Threatens(false, false, true, true, 300, 250, 2_500), Is.False, "Targets someone else.");
            Assert.That(BotPvpCrowdControlPolicy.Threatens(true, true, true, true, 300, 250, 2_500), Is.False, "Mezzed.");
        }

        [Test]
        public void PetsYieldToAPlayerPick()
        {
            // #175: a pet that hits a bot no longer takes it off its fight-mode pick.
            Assert.That(PetYields(TargetRole.Pet, TargetRole.Fighter), Is.True);
            Assert.That(PetYields(TargetRole.Pet, TargetRole.Healer), Is.True);
            Assert.That(PetYields(TargetRole.Pet, TargetRole.Pet), Is.False, "Pet against pet: the old rules decide.");
            Assert.That(PetYields(TargetRole.Fighter, TargetRole.Healer), Is.False, "Only pets yield; players keep the old rules.");
        }

        [Test]
        public void HiddenEnemiesStayCandidatesForSixSeconds()
        {
            // #176
            Assert.That(StillInSight(true, 10_000, -1), Is.True);
            Assert.That(StillInSight(false, 10_000, -1), Is.False, "Never seen.");
            Assert.That(StillInSight(false, 10_000, 5_000), Is.True);
            Assert.That(StillInSight(false, 11_000, 5_000), Is.False);
        }

        [Test]
        public void SupportLeashFromTheNearestCasterOrHealer()
        {
            // #177: Jay's numbers, casters 1,000 behind and the target 1,500 the other way.
            Assert.That(BeyondSupport(2_500, 1_500, leaderTarget: false, attackingGroupNearLeader: false), Is.True);
            Assert.That(BeyondSupport(1_400, 1_500, false, false), Is.False);
            Assert.That(BeyondSupport(2_500, 1_500, leaderTarget: true, attackingGroupNearLeader: false), Is.False, "The player's target.");
            Assert.That(BeyondSupport(2_500, 1_500, false, attackingGroupNearLeader: true), Is.False, "It is hitting the group.");
            Assert.That(BeyondSupport(int.MaxValue, 1_500, false, false), Is.False, "No back line in reach: only the player leash.");
        }

        [Test]
        public void CastersCloseUpOnTheirMelee()
        {
            Assert.That(CasterClosesIn(casting: false, underAttack: false, distanceToMelee: 1_800), Is.True);
            Assert.That(CasterClosesIn(false, false, 1_000), Is.False);
            Assert.That(CasterClosesIn(casting: true, underAttack: false, distanceToMelee: 1_800), Is.False, "Never walk out of a cast.");
            Assert.That(CasterClosesIn(false, underAttack: true, distanceToMelee: 1_800), Is.False, "Under attack: the kite rules decide.");
            Assert.That(CasterClosesIn(false, false, int.MaxValue), Is.False, "Nobody fighting in melee.");
        }

        [Test]
        public void CastersWalkForLineOfSight()
        {
            // #176: now, blockedUntil, sameTarget, inSightNow, distance
            Assert.That(BotLineOfSightPolicy.Walks(1_000, 4_000, true, false, 1_200), Is.True);
            Assert.That(BotLineOfSightPolicy.Walks(1_000, 4_000, true, true, 1_200), Is.False, "In sight again.");
            Assert.That(BotLineOfSightPolicy.Walks(5_000, 4_000, true, false, 1_200), Is.False, "The hold ran out.");
            Assert.That(BotLineOfSightPolicy.Walks(1_000, 4_000, false, false, 1_200), Is.False, "Another target.");
            Assert.That(BotLineOfSightPolicy.Walks(1_000, 4_000, true, false, 300), Is.False, "Close enough to cast.");
        }
    }
}
