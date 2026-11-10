using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerClass;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public class UT_BotSupportIdleCombat
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly IObjectDatabase EmptyDatabase =
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previous;
        private bool _previousIdleCombat;
        private readonly List<GameLiving> _actors = new();

        private sealed class Rules : NormalServerRules
        {
            public override bool IsAllowedToAttack(GameLiving attacker, GameLiving defender, bool quiet) =>
                attacker.Realm != defender.Realm;
        }

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
            protected override IServerRules ServerRulesImpl => new Rules();
        }

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public bool Attacking;
            public override byte Level { get => 50; set { } }
            public override bool IsAlive => true;
            public override bool IsAttacking => Attacking;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override GameObject TargetObject { get; set; }
            public override IControlledBrain ControlledBrain { get; set; }
            public override IPacketLib Out => new BotDummyPacketLib();
        }

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public ICharacterClass TestClass;
            public byte TestLevel = 30;
            public bool Alive = true;
            public int Stops;
            public int MeleeStarts;
            public int TestMana = 100;
            public int TestMaxMana = 100;
            public override bool IsAlive => Alive;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override bool IsCrowdControlled => false;
            public override bool InCombat => false;
            public override short MaxSpeed => 200;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override byte Level { get => TestLevel; set => TestLevel = value; }
            public override int EffectiveLevel => TestLevel;
            public override int MeleeAttackRange => 200;
            public override ICharacterClass CharacterClass => TestClass;
            public override IControlledBrain ControlledBrain { get; set; }
            public override void WalkTo(Vector3 target, short speed) { }
            public override void StopAttack() { Stops++; }
            public override void StartAttack(GameObject target) { MeleeStarts++; TargetObject = target; }
            public override void Follow(GameObject target, int minDistance, int maxDistance) { }
            public override int Mana { get => TestMana; set => TestMana = value; }
            public override int MaxMana => TestMaxMana;
            public override byte ManaPercent => TestMaxMana <= 0 ? (byte)0 : (byte)Math.Clamp(TestMana * 100 / TestMaxMana, 0, 100);
            public override byte EndurancePercent => 100;
            public override int GetModified(eProperty property) => property == eProperty.SpellRange ? 1000 : 0;
        }

        private sealed class Enemy : GameNPC
        {
            public byte TestLevel = 20;
            public override bool IsAlive => true;
            public override bool IsCasting => false;
            public override bool IsAttacking => false;
            public override int X => 100;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.None; set { } }
            public override byte Level { get => TestLevel; set => TestLevel = value; }
            public override int EffectiveLevel => TestLevel;
            public override GameObject TargetObject { get; set; }
        }

        private sealed class Brain : BotBrain
        {
            public bool? SpellResult;
            public eCheckSpellType? LastSpellType;
            public override bool CheckSpells(eCheckSpellType type)
            {
                LastSpellType = type;
                return SpellResult ?? false;
            }
            public override bool CanAggroTarget(GameLiving target) => target != null && target.IsAlive;
        }

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _previousIdleCombat = Properties.HEALER_IDLE_COMBAT;
            Properties.HEALER_IDLE_COMBAT = true;
        }

        [TearDown]
        public void Cleanup()
        {
            Properties.HEALER_IDLE_COMBAT = _previousIdleCombat;
            foreach (GameLiving actor in _actors)
                ServiceObjectStore.Remove(actor.effectListComponent);
            _actors.Clear();
            GameServer.LoadTestDouble(_previous);
        }

        [Test]
        public void SupportIdleAssistAllowedWhenGroupedHealthyAndFighting()
        {
            var (player, healer, enemy) = PartyWithHealer();
            player.Attacking = true;
            player.TargetObject = enemy;
            Assert.That(B(healer).MaySupportIdleCombatAssist(enemy), Is.True);
        }

        [Test]
        public void SupportIdleAssistRefusedWhenPropertyOff()
        {
            var (player, healer, enemy) = PartyWithHealer();
            player.Attacking = true;
            player.TargetObject = enemy;
            Properties.HEALER_IDLE_COMBAT = false;
            Assert.That(B(healer).MaySupportIdleCombatAssist(enemy), Is.False);
        }

        [Test]
        public void SupportIdleAssistRefusedWhenManaLow()
        {
            var (player, healer, enemy) = PartyWithHealer();
            player.Attacking = true;
            player.TargetObject = enemy;
            healer.TestMana = 30;
            healer.TestMaxMana = 100;
            Assert.That(healer.ManaPercent, Is.LessThan(BotBrain.SupportIdleAssistManaFloor));
            Assert.That(B(healer).MaySupportIdleCombatAssist(enemy), Is.False);
        }

        [Test]
        public void SupportIdleAssistRefusedWhenPassive()
        {
            var (player, healer, enemy) = PartyWithHealer();
            player.Attacking = true;
            player.TargetObject = enemy;
            healer.Stance = eBotStance.Passive;
            Assert.That(B(healer).MaySupportIdleCombatAssist(enemy), Is.False);
        }

        [Test]
        public void SupportAssistPlayerAttackJoinsAggroWhenIdleCombatOn()
        {
            var (player, healer, enemy) = PartyWithHealer();
            player.Attacking = true;
            player.TargetObject = enemy;
            int stopsBefore = healer.Stops;
            B(healer).AssistPlayerAttack(enemy);
            Assert.That(healer.Stops, Is.EqualTo(stopsBefore), "idle combat assist must not HoldSupportCombat");
            Assert.That(B(healer).HasAggro, Is.True);
        }

        [Test]
        public void SupportAssistPlayerAttackStillStopsWhenIdleCombatOff()
        {
            var (player, healer, enemy) = PartyWithHealer();
            player.Attacking = true;
            player.TargetObject = enemy;
            Properties.HEALER_IDLE_COMBAT = false;
            int stopsBefore = healer.Stops;
            B(healer).AssistPlayerAttack(enemy);
            Assert.That(healer.Stops, Is.GreaterThan(stopsBefore));
            Assert.That(B(healer).HasAggro, Is.False);
        }

        [Test]
        public void GroupNeedsResurrectionAttentionWhenCorpseAndRezSpellPresent()
        {
            var (_, healer, _) = PartyWithHealer();
            Bot dead = MakeBot(new ClassArmsman());
            Add(healer.Group, dead);
            dead.Alive = false;
            Field(typeof(GameBot), healer, "<ResurrectionSpell>k__BackingField",
                (Spell)RuntimeHelpers.GetUninitializedObject(typeof(Spell)));
            Assert.That(B(healer).GroupNeedsResurrectionAttention(), Is.True);
        }

        [Test]
        public void GroupNeedsResurrectionAttentionFalseWithoutSpell()
        {
            var (_, healer, _) = PartyWithHealer();
            Bot dead = MakeBot(new ClassArmsman());
            Add(healer.Group, dead);
            dead.Alive = false;
            Assert.That(B(healer).GroupNeedsResurrectionAttention(), Is.False);
        }

        private (Player, Bot, Enemy) PartyWithHealer()
        {
            Player player = Actor<Player>();
            var group = new Group(player);
            Add(group, player);
            Bot healer = MakeBot(new ClassCleric());
            Add(group, healer);
            healer.EnterPlayerLedGroup(player);
            Bot tank = MakeBot(new ClassPaladin());
            Add(group, tank);
            tank.EnterPlayerLedGroup(player);
            return (player, healer, Actor<Enemy>());
        }

        private Bot MakeBot(ICharacterClass characterClass)
        {
            Bot bot = Actor<Bot>();
            bot.Alive = true;
            bot.TestLevel = 30;
            bot.TestMana = 100;
            bot.TestMaxMana = 100;
            bot.Name = characterClass.Name;
            bot.TestClass = characterClass;
            var brain = new Brain { Body = bot };
            Field(typeof(GameNPC), bot, "m_ownBrain", brain);
            return bot;
        }

        private T Actor<T>() where T : GameLiving
        {
            T actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            if (actor is Enemy enemy)
                enemy.TestLevel = 20;
            actor.ObjectState = GameObject.eObjectState.Active;
            Field(typeof(GameLiving), actor, "<TempProperties>k__BackingField", new PropertyCollection());
            if (actor is GameNPC npc)
            {
                Field(typeof(GameNPC), actor, "m_brains", new ArrayList());
                Field(typeof(GameNPC), actor, "m_spells", new List<Spell>());
                npc.movementComponent = new NpcMovementComponent(npc);
            }
            actor.effectListComponent = EffectListComponent.Create(actor);
            _actors.Add(actor);
            return actor;
        }

        private static Brain B(Bot bot) => (Brain)bot.Brain;
        private static List<GameLiving> Members(Group group) =>
            (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);
        private static void Add(Group group, GameLiving member)
        {
            Members(group).Add(member);
            member.Group = group;
        }
        private static void Field(Type type, object owner, string name, object value) =>
            type.GetField(name, Hidden).SetValue(owner, value);
    }
}
