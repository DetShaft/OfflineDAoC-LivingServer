using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.Friends;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // /friend for autonomous bots (BotFriends): exact names in any case, same realm, parked bots through
    // offline_world_bots, never a retired bot, and a player character of the same name always wins.
    [TestFixture, NonParallelizable]
    public class UT_BotFriends
    {
        private string _path;
        private SqliteObjectDatabase _database;

        [SetUp]
        public void OpenDatabase()
        {
            // The test creates only this GUID-named SQLite file, never a server database.
            _path = Path.Combine(Path.GetTempPath(), "daoc-bot-friends-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            _database = new SqliteObjectDatabase($"Data Source={_path};Version=3;Pooling=False;");
            _database.RegisterDataObject(typeof(OfflineWorldBotRecord));
            _database.RegisterDataObject(typeof(DbCoreCharacterXCustomParam));
            _database.RegisterDataObject(typeof(DbCoreCharacter));

            AddBot("Haralhelm", eRealm.Midgard, level: 34, classId: 22, lastSaved: "2026-10-27T10:00:00.0000000Z", lastUpdate: "2026-10-27T12:30:00.0000000Z");
            AddBot("Brenna", eRealm.Hibernia, level: 12, classId: 43);
            AddBot("Oldsven", eRealm.Midgard, level: 50, classId: 23, retired: true);
            AddBot("Sharedname", eRealm.Midgard, level: 20, classId: 22);
            Assert.That(_database.AddObject(new DbCoreCharacter { Name = "Sharedname", AccountName = "player", Realm = (int)eRealm.Midgard }), Is.True);
        }

        [TearDown]
        public void DeleteDatabase()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            if (File.Exists(_path))
                File.Delete(_path);
        }

        [TestCase("Haralhelm")]
        [TestCase("haralhelm")]
        [TestCase("  HARALHELM ")]
        public void ParkedBotOfTheRealm_ResolvesToItsOwnName(string typed)
        {
            Assert.That(BotFriends.TryResolveName(_database, typed, eRealm.Midgard, out string botName), Is.True);
            Assert.That(botName, Is.EqualTo("Haralhelm"));
        }

        [TestCase("Haral")]
        [TestCase("Haralhelmx")]
        [TestCase("")]
        [TestCase("   ")]
        public void OnlyTheExactName_FindsABot(string typed)
        {
            Assert.That(BotFriends.TryResolveName(_database, typed, eRealm.Midgard, out string botName), Is.False);
            Assert.That(botName, Is.Null);
        }

        [Test]
        public void BotOfAnotherRealm_IsNotFound()
        {
            Assert.That(BotFriends.TryResolveName(_database, "Brenna", eRealm.Midgard, out _), Is.False);
            Assert.That(BotFriends.TryResolveName(_database, "Brenna", eRealm.Hibernia, out string botName), Is.True);
            Assert.That(botName, Is.EqualTo("Brenna"));
        }

        [Test]
        public void RetiredBot_IsNeverFound()
        {
            Assert.That(BotFriends.TryResolveName(_database, "Oldsven", eRealm.Midgard, out _), Is.False);
            Assert.That(BotFriends.FindRecord(_database, "Oldsven", eRealm.Midgard), Is.Null);
        }

        [TestCase("Sharedname")]
        [TestCase("sharedname")]
        public void PlayerCharacterOfTheSameName_Wins(string typed)
        {
            Assert.That(BotFriends.HasCharacter(_database, typed), Is.True);
            Assert.That(BotFriends.TryResolveName(_database, typed, eRealm.Midgard, out _), Is.False);
        }

        [Test]
        public void OfflineStatuses_ListOnlyTheBotsOfTheRealmThatAreNoCharacter()
        {
            string[] friends = ["Haralhelm", "Brenna", "Oldsven", "Sharedname", "Nobody"];
            FriendStatus[] statuses = BotFriends.OfflineStatuses(_database, friends, ["Sharedname"], eRealm.Midgard);

            Assert.That(statuses.Select(status => status.Name), Is.EquivalentTo(new[] { "Haralhelm" }));
            Assert.That(statuses[0].Level, Is.EqualTo(34));
            Assert.That(statuses[0].ClassID, Is.EqualTo(22));
            Assert.That(statuses[0].LastPlayed.ToUniversalTime(), Is.EqualTo(new DateTime(2026, 10, 27, 12, 30, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void OfflineStatuses_WithOnlyCharacters_AskNothing()
        {
            Assert.That(BotFriends.OfflineStatuses(_database, ["Sharedname"], ["sharedname"], eRealm.Midgard), Is.Empty);
        }

        [Test]
        public void LastPlayed_IsTheLaterStamp_OrMinValueWithoutOne()
        {
            var record = new OfflineWorldBotRecord { LastSavedUtc = "2026-10-28T09:00:00.0000000Z", LastUpdateUtc = "2026-10-27T09:00:00.0000000Z" };
            Assert.That(BotFriends.LastPlayed(record).ToUniversalTime(), Is.EqualTo(new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc)));

            Assert.That(BotFriends.LastPlayed(new OfflineWorldBotRecord { LastSavedUtc = "", LastUpdateUtc = "not a date" }), Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void CharacterNames_FindOnlyCharacters()
        {
            Assert.That(BotFriends.CharacterNames(_database, ["Sharedname", "Haralhelm"]), Is.EquivalentTo(new[] { "Sharedname" }));
            Assert.That(BotFriends.CharacterNames(_database, []), Is.Empty);
        }

        [Test]
        public void Notices_GoOnlyToPlayersOfTheRealmWithTheBotOnTheirList()
        {
            Assert.That(BotFriends.IsListener(eRealm.Midgard, ["haralhelm"], eRealm.Midgard, "Haralhelm"), Is.True);
            Assert.That(BotFriends.IsListener(eRealm.Albion, ["Haralhelm"], eRealm.Midgard, "Haralhelm"), Is.False);
            Assert.That(BotFriends.IsListener(eRealm.Midgard, ["Brenna"], eRealm.Midgard, "Haralhelm"), Is.False);
        }

        [Test]
        public void SocialLine_HasUpstreamsFormat()
        {
            Assert.That(BotFriends.SocialLine(3, "Haralhelm", 34, 22, "Vale of Mularn"), Is.EqualTo("F,3,Haralhelm,34,22,\"Vale of Mularn\""));
        }

        [Test]
        public void WhereWithActivity_JoinsZoneAndLabel()
        {
            Assert.That(BotFriends.WhereWithActivity("Vale of Mularn", "grinding"), Is.EqualTo("Vale of Mularn — grinding"));
            Assert.That(BotFriends.WhereWithActivity("Vale of Mularn", ""), Is.EqualTo("Vale of Mularn"));
            Assert.That(BotFriends.WhereWithActivity("", "idle in town"), Is.EqualTo("idle in town"));
        }

        [TestCase(false, false, "Town idling", "Idle in Mag Mell", "idle in town")]
        [TestCase(true, true, "Following the player's group", "", "in group")]
        [TestCase(true, false, "Waiting for group members", "", "in group")]
        [TestCase(false, false, "Grinding bandits", "Kill bandits", "grinding")]
        [TestCase(false, false, "Marching to the frontier", "Keep take", "frontier")]
        [TestCase(false, false, "Selling at the vendor", "", "in town")]
        [TestCase(false, false, "", "", "online")]
        public void ActivityLabel_SummarizesPlannerText(bool inGroup, bool playerLed, string activity, string goal, string expected)
        {
            Assert.That(BotFriends.ActivityLabel(inGroup, playerLed, activity, goal), Is.EqualTo(expected));
        }

        [TestCase(true, (byte)1)]
        [TestCase(false, (byte)0)]
        public void FriendNotice_HasTheOldClientsFormat(bool entered, byte flag)
        {
            // game.dll's 0xC6 handler: a byte (1 "just entered", 0 "just left"), then the name as a 4-byte length string.
            using var pak = new GSTCPPacketOut().Init(0xC6);
            long header = pak.Length;
            BotFriends.WriteFriendNotice(pak, entered, "Haralhelm");

            byte[] body = pak.ToArray().Skip((int)header).ToArray();
            byte[] expected = new byte[] { flag, 10, 0, 0, 0 }.Concat("Haralhelm"u8.ToArray()).Append((byte)0).ToArray();
            Assert.That(body, Is.EqualTo(expected));
        }

        [TestCase("Haralhelm", true)]
        [TestCase(" haralhelm ", true)]
        [TestCase("Brenna", false)]
        [TestCase("", false)]
        public void AddedFriend_IsAnnouncedOnlyWhenOnline(string friend, bool announced)
        {
            // A friend just added gets the "just entered" notice only when it is in the online list Friends Monitor gets.
            Assert.That(BotFriends.IsOnlineFriend(["Haralhelm", "Dowie"], friend), Is.EqualTo(announced));
        }

        [Test]
        public async Task EmptyFriendsList_StillGetsAStatusCacheEntry()
        {
            // Upstream returned before the status cache had an entry, so the first friend added got no offline status
            // ("was not registered in Friends Manager Status Cache", in game 2026-10-28).
            using var server = new EpicTestServerScope();
            var manager = new FriendsManager(_database);
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            typeof(GamePlayer).GetField("m_dbCharacter", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, new DbCoreCharacter { Name = "Dowie", SerializedFriendsList = string.Empty });

            await manager.AddPlayerFriendsListToCache(player);

            var statuses = (ConcurrentDictionary<GamePlayer, FriendStatus[]>)typeof(FriendsManager)
                .GetProperty("PlayersFriendsStatusCache", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(manager);
            Assert.That(statuses.TryGetValue(player, out FriendStatus[] entry), Is.True);
            Assert.That(entry, Is.Empty);
            Assert.That(manager[player], Is.Empty);
        }

        // /friend target (the Friends window's ADD TARGET): hasTarget, isSelf, isPlayer, isGamebot, isCompanion, isOwnHelper,
        // sameRealm, alreadyFriend.
        [TestCase(false, false, false, false, false, false, false, false, BotFriends.TargetFriend.NoTarget)]
        [TestCase(true, true, true, false, false, false, true, false, BotFriends.TargetFriend.Self)]
        [TestCase(true, false, false, false, true, false, true, false, BotFriends.TargetFriend.Companion)]
        [TestCase(true, false, false, false, true, true, true, false, BotFriends.TargetFriend.AddHelper)]
        [TestCase(true, false, false, false, true, true, true, true, BotFriends.TargetFriend.AlreadyFriend)]
        [TestCase(true, false, false, false, false, false, false, false, BotFriends.TargetFriend.NotACharacter)]
        [TestCase(true, false, true, false, false, false, false, false, BotFriends.TargetFriend.OtherRealm)]
        [TestCase(true, false, false, true, false, false, false, false, BotFriends.TargetFriend.OtherRealm)]
        [TestCase(true, false, false, true, false, false, true, true, BotFriends.TargetFriend.AlreadyFriend)]
        [TestCase(true, false, true, false, false, false, true, false, BotFriends.TargetFriend.Add)]
        [TestCase(true, false, false, true, false, false, true, false, BotFriends.TargetFriend.Add)]
        public void AddTarget_AddsOnlyPlayersGamebotsAndYourOwnHelpersOfTheRealm(bool hasTarget, bool isSelf, bool isPlayer,
            bool isGamebot, bool isCompanion, bool isOwnHelper, bool sameRealm, bool alreadyFriend, BotFriends.TargetFriend expected)
        {
            Assert.That(BotFriends.ClassifyTarget(hasTarget, isSelf, isPlayer, isGamebot, isCompanion, isOwnHelper, sameRealm, alreadyFriend),
                Is.EqualTo(expected));
        }

        [Test]
        public void AddTarget_NeverTakesAFriendOff()
        {
            // Typing /friend name toggles; the button must not: a friend already listed only gets a message.
            Assert.That(BotFriends.ClassifyTarget(true, false, true, false, false, false, true, true), Is.EqualTo(BotFriends.TargetFriend.AlreadyFriend));
            Assert.That(BotFriends.TargetMessage(BotFriends.TargetFriend.AlreadyFriend, "Haralhelm"), Does.Contain("already"));
        }

        // Only your own /spawn helper of your realm becomes a gamebot: temporaryHelper, epicSpecialForce, autonomous,
        // ownedByPlayer, sameRealm.
        [TestCase(true, false, false, true, true, true)]
        [TestCase(true, false, false, false, true, false)]
        [TestCase(true, false, false, true, false, false)]
        [TestCase(false, true, true, false, true, false)]
        [TestCase(false, false, true, false, true, false)]
        [TestCase(false, false, false, true, true, false)]
        public void OnlyYourOwnHelper_BecomesAGamebot(bool helper, bool epic, bool autonomous, bool owned, bool sameRealm, bool expected)
        {
            Assert.That(BefriendedHelpers.IsConvertible(helper, epic, autonomous, owned, sameRealm), Is.EqualTo(expected));
        }

        [TestCase("Haralhelm", false)]
        [TestCase("oldsven", false)]
        [TestCase("Brenna", false)]
        [TestCase("Sharedname", false)]
        [TestCase("Freshname", true)]
        [TestCase("", false)]
        public void HelperName_MustBeFreeOfCharactersAndGamebots(string name, bool free)
        {
            // Any realm and retired bots too: a gamebot name is never given twice.
            Assert.That(BefriendedHelpers.IsNameFree(_database, name), Is.EqualTo(free));
        }

        [TestCase(BotFriends.TargetFriend.NoTarget)]
        [TestCase(BotFriends.TargetFriend.Self)]
        [TestCase(BotFriends.TargetFriend.Companion)]
        [TestCase(BotFriends.TargetFriend.NotACharacter)]
        [TestCase(BotFriends.TargetFriend.OtherRealm)]
        [TestCase(BotFriends.TargetFriend.AlreadyFriend)]
        public void AddTarget_EveryRefusalSaysWhy(BotFriends.TargetFriend result)
        {
            Assert.That(BotFriends.TargetMessage(result, "Haralhelm"), Is.Not.Empty);
        }

        [Test]
        public void ReleasedHelper_LogsInAtOnceAndStaysOnlineForItsFirstSession()
        {
            // Issue #39: Kettleboil, released at 4:52 a.m. with the clock schedule on, waited for her slot in the evening.
            BefriendedHelpers.ResetForTests();
            try
            {
                DateTime now = new(2026, 11, 12, 10, 52, 0, DateTimeKind.Utc);
                Assert.That(BefriendedHelpers.LogsInNow(601, now), Is.False);

                TimeSpan session = BefriendedHelpers.FirstSessionLength(0.5);
                BefriendedHelpers.SetFirstSessionForTests(601, now + session);
                Assert.That(BefriendedHelpers.LogsInNow(601, now), Is.True);
                Assert.That(BefriendedHelpers.KeepsOnline(601, now.AddMinutes(59)), Is.True);
                Assert.That(AutonomousBotSchedule.WantsOnline(new OfflineWorldBotRecord { BotId = 601 }, now), Is.True);
                Assert.That(BefriendedHelpers.LogsInNow(602, now), Is.False);

                // Then the schedule decides again, as for every gamebot.
                Assert.That(BefriendedHelpers.KeepsOnline(601, now + session), Is.False);
            }
            finally
            {
                BefriendedHelpers.ResetForTests();
            }
        }

        [TestCase(0.0, 120)]
        [TestCase(0.5, 150)]
        [TestCase(1.0, 180)]
        [TestCase(-1.0, 120)]
        [TestCase(2.0, 180)]
        public void FirstSession_LastsTwoToThreeHours(double roll, int minutes)
        {
            // Owner 2026-11-12: "leave em online for 2-3 hours upon first befriending".
            Assert.That(BefriendedHelpers.FirstSessionLength(roll), Is.EqualTo(TimeSpan.FromMinutes(minutes)));
        }

        [Test]
        public void StarterKit_OnlyForABefriendedHelperWithNoEquipment()
        {
            // Issue #39: Kettleboil's gear was never saved; she gets a kit for her level once, on her first login.
            string origin = "Was Ferac's " + BefriendedHelpers.OriginNote;
            Assert.That(BefriendedHelpers.NeedsStarterKitFor(origin, []), Is.True);
            Assert.That(BefriendedHelpers.NeedsStarterKitFor(origin, [40, 41]), Is.True);                       // backpack only
            Assert.That(BefriendedHelpers.NeedsStarterKitFor(origin, [(int)eInventorySlot.TorsoArmor]), Is.False);
            Assert.That(BefriendedHelpers.NeedsStarterKitFor(origin, [(int)eInventorySlot.RightHandWeapon]), Is.False);
            Assert.That(BefriendedHelpers.NeedsStarterKitFor("Entered world through staggered login queue", []), Is.False);
            Assert.That(BefriendedHelpers.NeedsStarterKitFor((string)null, []), Is.False);
        }

        private void AddBot(string name, eRealm realm, int level, int classId, bool retired = false, string lastSaved = "", string lastUpdate = "")
        {
            Assert.That(_database.AddObject(new OfflineWorldBotRecord
            {
                Name = name, Realm = (int)realm, Level = level, ClassId = classId, IsRetired = retired,
                LastSavedUtc = lastSaved, LastUpdateUtc = lastUpdate,
            }), Is.True);
        }
    }
}
