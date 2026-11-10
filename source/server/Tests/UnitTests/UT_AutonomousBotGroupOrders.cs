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
    public class UT_AutonomousBotGroupOrders
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly IObjectDatabase EmptyDatabase =
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previous;
        private bool _previousOrders;
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
            public int MeleeStarts;
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
            public override void StopAttack() { }
            public override void StartAttack(GameObject target) { MeleeStarts++; TargetObject = target; }
            public override void Follow(GameObject target, int minDistance, int maxDistance) { }
            public override int Mana { get; set; } = 100;
            public override byte EndurancePercent => 100;
            public override int GetModified(eProperty property) => property == eProperty.SpellRange ? 1000 : 0;
        }

        private sealed class Enemy : GameNPC
        {
            public override bool IsAlive => true;
            public override bool IsCasting => false;
            public override bool IsAttacking => false;
            public override int X => 100;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.None; set { } }
            public override byte Level { get => 20; set { } }
            public override int EffectiveLevel => 20;
        }

        private sealed class Brain : BotBrain
        {
            public int AssistOrders;
            public int BuffOrders;
            public int FollowOrders;
            public GameLiving LastAssistTarget;
            public override bool CanAggroTarget(GameLiving target) => target != null && target.IsAlive;
            public override void OrderAssistCombat(GameLiving target)
            {
                AssistOrders++;
                LastAssistTarget = target;
                // Count only — full AttackMostWanted needs a live attack component.
            }
            public override void OrderBuffs()
            {
                BuffOrders++;
            }
            public override void OrderFollow(GamePlayer leader)
            {
                FollowOrders++;
                base.OrderFollow(leader);
            }
        }

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _previousOrders = Properties.BOT_GROUP_ORDERS;
            Properties.BOT_GROUP_ORDERS = true;
            Properties.HEALER_IDLE_COMBAT = true;
        }

        [TearDown]
        public void Cleanup()
        {
            Properties.BOT_GROUP_ORDERS = _previousOrders;
            foreach (GameLiving actor in _actors)
                ServiceObjectStore.Remove(actor.effectListComponent);
            _actors.Clear();
            GameServer.LoadTestDouble(_previous);
        }

        [TestCase("attack", AutonomousBotGroupOrders.OrderKind.Attack)]
        [TestCase("attack it", AutonomousBotGroupOrders.OrderKind.Attack)]
        [TestCase("assist me", AutonomousBotGroupOrders.OrderKind.Assist)]
        [TestCase("assist", AutonomousBotGroupOrders.OrderKind.Assist)]
        [TestCase("buff us", AutonomousBotGroupOrders.OrderKind.Buff)]
        [TestCase("buff", AutonomousBotGroupOrders.OrderKind.Buff)]
        [TestCase("follow me", AutonomousBotGroupOrders.OrderKind.Follow)]
        [TestCase("follow", AutonomousBotGroupOrders.OrderKind.Follow)]
        [TestCase("stay", AutonomousBotGroupOrders.OrderKind.Stay)]
        [TestCase("stay here", AutonomousBotGroupOrders.OrderKind.Stay)]
        [TestCase("hold position", AutonomousBotGroupOrders.OrderKind.Stay)]
        [TestCase("pls follow me", AutonomousBotGroupOrders.OrderKind.Follow)]
        public void ParsesFixedVocabulary(string message, AutonomousBotGroupOrders.OrderKind expected) =>
            Assert.That(AutonomousBotGroupOrders.Parse(message), Is.EqualTo(expected));

        [TestCase("heal me")]
        [TestCase("attack the red con by the bridge please now")]
        [TestCase("mobs pull hard here")]
        [TestCase("can u come to mularn")]
        [TestCase("buff the tank with str")]
        [TestCase("")]
        public void RejectsAmbiguousOrOutOfVocabulary(string message) =>
            Assert.That(AutonomousBotGroupOrders.Parse(message), Is.EqualTo(AutonomousBotGroupOrders.OrderKind.None));

        [Test]
        public void LeaderAssistOrderEngagesGroupedBot()
        {
            var (player, damage, enemy) = Party();
            player.Attacking = true;
            player.TargetObject = enemy;
            Assert.That(AutonomousBotGroupOrders.TryHandle(player, "assist me", out GameBot speaker, out string ack), Is.True);
            Assert.That(speaker, Is.SameAs(damage));
            Assert.That(ack, Is.EqualTo("attacking"));
            Assert.That(B(damage).AssistOrders, Is.EqualTo(1));
            Assert.That(B(damage).LastAssistTarget, Is.SameAs(enemy));
        }

        [Test]
        public void FollowAndStayOrdersReachBrain()
        {
            var (player, damage, _) = Party();
            Assert.That(AutonomousBotGroupOrders.TryHandle(player, "follow me", out _, out string followAck), Is.True);
            Assert.That(followAck, Is.EqualTo("following"));
            Assert.That(B(damage).FollowOrders, Is.EqualTo(1));
            Assert.That(AutonomousBotGroupOrders.TryHandle(player, "stay", out _, out string stayAck), Is.True);
            Assert.That(stayAck, Is.EqualTo("staying"));
        }

        [Test]
        public void BuffOrderReachesBrain()
        {
            var (player, damage, _) = Party();
            Assert.That(AutonomousBotGroupOrders.TryHandle(player, "buff us", out _, out string ack), Is.True);
            Assert.That(ack, Is.EqualTo("buffing"));
            Assert.That(B(damage).BuffOrders, Is.EqualTo(1));
        }

        [Test]
        public void NonLeaderCannotOrder()
        {
            var (player, damage, enemy) = Party();
            Player other = Actor<Player>();
            Add(player.Group, other);
            // Group.Leader stays the creator (player); other is not leader.
            player.TargetObject = enemy;
            Assert.That(AutonomousBotGroupOrders.TryHandle(other, "assist me", out _, out _), Is.False);
            Assert.That(B(damage).AssistOrders, Is.Zero);
        }

        [Test]
        public void PropertyOffDisablesOrders()
        {
            var (player, damage, enemy) = Party();
            player.TargetObject = enemy;
            Properties.BOT_GROUP_ORDERS = false;
            Assert.That(AutonomousBotGroupOrders.TryHandle(player, "attack", out _, out _), Is.False);
            Assert.That(B(damage).AssistOrders, Is.Zero);
        }

        [Test]
        public void AttackWithoutTargetAcknowledgesNoTarget()
        {
            var (player, damage, _) = Party();
            player.TargetObject = null;
            Assert.That(AutonomousBotGroupOrders.TryHandle(player, "attack", out _, out string ack), Is.True);
            Assert.That(ack, Is.EqualTo("no target"));
            Assert.That(B(damage).AssistOrders, Is.Zero);
        }

        private (Player, Bot, Enemy) Party()
        {
            Player player = Actor<Player>();
            var group = new Group(player);
            Add(group, player);
            Bot damage = MakeBot(new ClassArmsman());
            Add(group, damage);
            damage.EnterPlayerLedGroup(player);
            return (player, damage, Actor<Enemy>());
        }

        private Bot MakeBot(ICharacterClass characterClass)
        {
            Bot bot = Actor<Bot>();
            bot.Alive = true;
            bot.TestLevel = 30;
            bot.Name = characterClass.Name;
            bot.TestClass = characterClass;
            var brain = new Brain { Body = bot };
            Field(typeof(GameNPC), bot, "m_ownBrain", brain);
            return bot;
        }

        private T Actor<T>() where T : GameLiving
        {
            T actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
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
