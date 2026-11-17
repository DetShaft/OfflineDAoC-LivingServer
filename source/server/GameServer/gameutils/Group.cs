using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;
using DOL.GS.ServerProperties;
using DOL.Language;
using static DOL.GS.GameObject;

namespace DOL.GS
{
    /// <summary>
    /// This class represents a Group inside the game
    /// </summary>
    public class Group : IGameStaticItemOwner
    {
        private readonly List<GameLiving> _groupMembers;
        private readonly Lock _groupMembersLock = new();
        private AbstractMission _mission;
        public GamePlayer CompanionRaidOwner { get; private set; }
        public bool IsCompanionRaid => CompanionRaidOwner != null;
        private int _companionRaidCapacity = 40;
        public int MaximumMemberCount => IsCompanionRaid ? _companionRaidCapacity : Properties.GROUP_MAX_MEMBER;

        public bool EnableCompanionRaid(GamePlayer owner, int capacity = 40)
        {
            lock (_groupMembersLock)
            {
                if (capacity is not (40 or 80) || _groupMembers.Count > capacity ||
                    owner == null || owner.Level != 50 || LivingLeader != owner ||
                    !_groupMembers.Contains(owner) || _groupMembers.Any(member => member != owner &&
                        (member is not GameBot bot || !bot.IsTemporaryGroupHelper || bot.IsAutonomousWorldBot || bot.Owner != owner)))
                    return false;
                CompanionRaidOwner = owner;
                _companionRaidCapacity = capacity;
                return true;
            }
        }

        public byte MemberCount
        {
            get
            {
                lock (_groupMembersLock)
                {
                    return (byte) _groupMembers.Count;
                }
            }
        }

        public GameLiving LivingLeader { get; private set; }

        public GamePlayer Leader
        {
            get => LivingLeader as GamePlayer;
            private set => LivingLeader = value;
        }

        public bool AutosplitLoot { get; set; } = true;
        public bool AutosplitCoins { get; set; } = true;
        private int _lootTurn;
        private readonly Lock _lootTurnLock = new();
        public byte Status { get; set; } = 0x0A;

        public AbstractMission Mission
        {
            get => _mission;
            set
            {
                _mission = value;

                foreach (GamePlayer groupMember in GetPlayersInTheGroup())
                {
                    groupMember.Out.SendQuestListUpdate();

                    if (value != null)
                        groupMember.Out.SendMessage(_mission.Description, eChatType.CT_System, eChatLoc.CL_SystemWindow);
                }
            }
        }

        public Group(GamePlayer leader) : this((GameLiving) leader) { }

        public Group(GameLiving leader)
        {
            LivingLeader = leader;
            _groupMembers = new(Properties.GROUP_MAX_MEMBER);
        }

        public List<GameLiving> GetMembersInTheGroup()
        {
            List<GameLiving> temp = GameLoop.GetListForTick<GameLiving>();

            lock (_groupMembersLock)
            {
                temp.AddRange(_groupMembers);
            }

            return temp;
        }

        public List<GamePlayer> GetPlayersInTheGroup()
        {
            List<GamePlayer> temp = GameLoop.GetListForTick<GamePlayer>();

            lock (_groupMembersLock)
            {
                foreach (GameLiving groupMember in _groupMembers)
                {
                    if (groupMember is GamePlayer player)
                        temp.Add(player);
                }
            }

            return temp;
        }

        public bool AddMember(GameLiving living)
        {
            int memberCount;

            lock (_groupMembersLock)
            {
                if (_groupMembers.Count >= MaximumMemberCount || living.Group != null)
                    return false;
                if (IsCompanionRaid && living != CompanionRaidOwner &&
                    (living is not GameBot companion || !companion.IsTemporaryGroupHelper || companion.IsAutonomousWorldBot || companion.Owner != CompanionRaidOwner))
                    return false;

                _groupMembers.Add(living);
                memberCount = _groupMembers.Count;
                living.Group = this;
                living.GroupIndex = (byte) (memberCount - 1);
            }

            SendMessageToGroupMembers($"{living.Name} has joined the group.", eChatType.CT_System, eChatLoc.CL_SystemWindow);

            if (living is GamePlayer player)
            {
                player.Duel?.Stop();
                player.Out.SendGroupMembersUpdate(true, true);

                // Part of the hack to make friendly pets untargetable (or targetable again) with TAB on a PvP server.
                // We could also check for non controlled pets (turrets for example) around the player, but it isn't very important.
                if (GameServer.Instance.Configuration.ServerType is EGameServerType.GST_PvP)
                {
                    IControlledBrain controlledBrain = player.ControlledBrain;
                    Guild playerGuild = player.Guild;
                    bool updateOneself = false;

                    // Update how the added player sees their pet and themself.
                    if (controlledBrain != null)
                    {
                        SendControlledBodyGuildID(player, playerGuild, controlledBrain.Body);
                        updateOneself = true;
                    }

                    // Let's all be friends.
                    foreach (GamePlayer groupMember in GetPlayersInTheGroup())
                    {
                        if (groupMember == living)
                            continue;

                        Guild groupMemberGuild = groupMember.Guild;

                        if (controlledBrain != null)
                        {
                            // Update how the group member sees the added player's pet and themself.
                            SendControlledBodyGuildID(groupMember, groupMemberGuild, controlledBrain.Body);
                            groupMember.Out.SendObjectGuildID(groupMember, groupMemberGuild ?? Guild.DummyGuild);
                        }

                        IControlledBrain groupMemberControlledBrain = groupMember.ControlledBrain;

                        if (groupMemberControlledBrain != null)
                        {
                            // Update how the added player sees the group member's pet and themself.
                            SendControlledBodyGuildID(player, playerGuild, groupMemberControlledBrain.Body);
                            updateOneself = true;
                        }
                    }

                    if (updateOneself)
                        player.Out.SendObjectGuildID(player, playerGuild ?? Guild.DummyGuild);
                }
            }

            UpdateMember(living, true, true);
            UpdateGroupWindow();
            GameEventMgr.Notify(GroupEvent.MemberJoined, this, new MemberJoinedEventArgs(living));
            return true;
        }

        public bool RemoveMember(GameLiving living, bool retainSingleRemainingMember = false)
        {
            bool disbandEmptyGroup;
            lock (_groupMembersLock)
            {
                if (!_groupMembers.Remove(living))
                    return false;

                living.Group = null;
                living.GroupIndex = 0xFF;
                disbandEmptyGroup = _groupMembers.Count < 1;
            }

            // Never enter the autonomous coordinator while holding the group
            // member lock. Its session cleanup takes the coordinator lock and
            // then snapshots group membership; doing the inverse here can
            // deadlock a game-loop worker under population load.
            if (disbandEmptyGroup)
                DisbandGroup();

            SendMessageToGroupMembers($"{living.Name} has left the group.", eChatType.CT_System, eChatLoc.CL_SystemWindow);

            if (living is GameBot removedBot)
            {
                removedBot.LeavePlayerLedGroup();
                if (removedBot.Brain is DOL.AI.Brain.BotBrain removedBrain)
                    removedBrain.FSM.SetCurrentState(eFSMStateType.IDLE);
            }

            AutonomousBotGroupCoordinator.OnMemberRemoved(this, living);

            // Update Player.
            if (living is GamePlayer player)
            {
                player.Out.SendGroupWindowUpdate();
                player.Out.SendQuestListUpdate();

                List<ECSGameAbilityEffect> abilityEffects = player.effectListComponent.GetAbilityEffects();

                // Cancel ability effects.
                foreach (ECSGameAbilityEffect abilityEffect in abilityEffects)
                {
                    switch (abilityEffect.EffectType)
                    {
                        case eEffect.Guard:
                        {
                            if (abilityEffect is GuardECSGameEffect guard)
                            {
                                if (guard.Source is GamePlayer && guard.Target is GamePlayer)
                                    _ = guard.End();
                            }

                            continue;
                        }
                        case eEffect.Protect:
                        {
                            if (abilityEffect is ProtectECSGameEffect protect)
                            {
                                if (protect.Source is GamePlayer && protect.Target is GamePlayer)
                                    _ = protect.End();
                            }

                            continue;
                        }
                        case eEffect.Intercept:
                        {
                            if (abilityEffect is InterceptECSGameEffect intercept)
                            {
                                if (intercept.Source is GamePlayer && intercept.Target is GamePlayer)
                                    _ = intercept.End();
                            }

                            continue;
                        }
                    }
                }

                // Part of the hack to make friendly pets untargetable (or targetable again) with TAB on a PvP server.
                // We could also check for non controlled pets (turrets for example) around the player, but it isn't very important.
                if (GameServer.Instance.Configuration.ServerType is EGameServerType.GST_PvP)
                {
                    IControlledBrain controlledBrain = player.ControlledBrain;
                    Guild playerGuild = player.Guild;
                    bool updateOneself = false;

                    // Update how the removed player sees their pet and themself.
                    if (controlledBrain != null)
                    {
                        SendControlledBodyGuildID(player, playerGuild, controlledBrain.Body);
                        updateOneself = true;
                    }

                    foreach (GamePlayer groupMember in GetPlayersInTheGroup())
                    {
                        if (groupMember == living)
                            continue;

                        Guild groupMemberGuild = groupMember.Guild;

                        if (playerGuild == null || groupMemberGuild == null || playerGuild != groupMemberGuild)
                        {
                            // Update how the group member sees the removed player's pet.
                            // There shouldn't be any need to update them.
                            if (controlledBrain != null)
                                SendControlledBodyGuildID(groupMember, playerGuild, controlledBrain.Body);

                            IControlledBrain groupMemberControlledBrain = groupMember.ControlledBrain;

                            // Update how the removed player sees the group member's pet and themself.
                            if (groupMemberControlledBrain != null)
                            {
                                SendControlledBodyGuildID(player, groupMemberGuild, groupMemberControlledBrain.Body);
                                updateOneself = true;
                            }
                        }
                    }

                    if (updateOneself)
                        player.Out.SendObjectGuildID(player, playerGuild ?? Guild.DummyGuild);
                }

                player.Out.SendMessage("You leave your group.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                player.Notify(GamePlayerEvent.LeaveGroup, player);
            }

            GameLiving soleMemberToRemove = null;
            string newLeaderName = null;
            lock (_groupMembersLock)
            {
                int memberCount = _groupMembers.Count;

                if (memberCount == 1 && !retainSingleRemainingMember)
                {
                    // Remove outside this lock. RemoveMember notifies the
                    // autonomous coordinator, whose lock order is coordinator
                    // then group.
                    soleMemberToRemove = _groupMembers[0];
                }
                else if (memberCount == 1 && LivingLeader == living)
                {
                    // Autonomous RvR tenure expiry can intentionally leave a
                    // one-bot warband alive until its remaining member's own
                    // independent tenure ends.
                    LivingLeader = _groupMembers[0];
                }
                else if (memberCount > 1 && LivingLeader == living)
                {
                    // Assign a new leader.
                    // Autonomous bot-only groups have no GamePlayer member;
                    // use one when present, otherwise elect the first remaining
                    // living member instead of throwing on an empty sequence.
                    LivingLeader = _groupMembers.OfType<GamePlayer>().FirstOrDefault() ??
                                   _groupMembers.FirstOrDefault(member => member.IsAlive) ??
                                   _groupMembers[0];
                    newLeaderName = LivingLeader.Name;
                }
            }

            if (soleMemberToRemove != null)
                _ = RemoveMember(soleMemberToRemove);
            else if (newLeaderName != null)
                SendMessageToGroupMembers($"{newLeaderName} is the new group leader.", eChatType.CT_System, eChatLoc.CL_SystemWindow);

            // Gamebots a player invited follow that player. Leaving or /disband used to clear that only when the group
            // fell to one member, so two or more bots kept following someone they were no longer grouped with and
            // refused new invites until the player logged out (issue #33).
            if (living is GamePlayer leavingPlayer)
                ReleaseGamebotsLedBy(leavingPlayer);

            UpdateGroupIndexes();
            UpdateGroupWindow();
            GameEventMgr.Notify(GroupEvent.MemberDisbanded, this, new MemberDisbandedEventArgs(living));
            // Temporary /spawn companions have no persistent character record.
            // Delete once, after native group bookkeeping and notifications are
            // complete. Deleting earlier re-entered RemoveMember from
            // GameBot.Delete and could run cleanup multiple times.
            if (living is GameBot { IsTemporaryGroupHelper: true } temporaryHelper)
                temporaryHelper.Delete();
            return true;
        }

        // Part of the hack to make friendly pets untargetable (or targetable again) with TAB on a PvP server.
        // Calls 'SendObjectGuildID' on the player and looks for all controlled NPCs controlled by 'controlledBody' recursively.
        private static void SendControlledBodyGuildID(GamePlayer player, Guild playerGuild, GameNPC controlledBody)
        {
            IControlledBrain[] npcControlledBrains = controlledBody.ControlledNpcList;

            if (npcControlledBrains != null)
            {
                foreach (IControlledBrain npcControlledBrain in npcControlledBrains.Where(x => x != null))
                    SendControlledBodyGuildID(player, playerGuild, npcControlledBrain.Body);
            }

            player.Out.SendObjectGuildID(controlledBody, playerGuild ?? Guild.DummyGuild);
        }

        /// <summary>Invited gamebots still in this group that follow <paramref name="player"/> (issue #33).</summary>
        public List<GameBot> GamebotsLedBy(GamePlayer player)
        {
            lock (_groupMembersLock)
                return _groupMembers.OfType<GameBot>()
                    .Where(bot => bot.PlayerGroupLeader == player && bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper)
                    .ToList();
        }

        /// <summary>
        /// After <paramref name="player"/> leaves, the gamebots that followed them follow another player of their realm
        /// still in the group, or, with none left, leave the group and go back to their own play.
        /// </summary>
        public void ReleaseGamebotsLedBy(GamePlayer player)
        {
            List<GameBot> followers = GamebotsLedBy(player);
            if (followers.Count == 0)
                return;

            GamePlayer stays;
            lock (_groupMembersLock)
                stays = _groupMembers.OfType<GamePlayer>().FirstOrDefault(member => member != player);

            foreach (GameBot bot in followers)
            {
                if (stays != null && bot.EnterPlayerLedGroup(stays))
                    continue;
                // Outside the member lock, as for the sole remaining member above. RemoveMember clears the follow
                // order, idles the bot and hands it back to the autonomous coordinator; a bot the last removal
                // already took out is skipped there.
                _ = RemoveMember(bot);
            }
        }

        public void DisbandGroup()
        {
            AutonomousBotGroupCoordinator.OnDisbanding(this);
            _ = GroupMgr.RemoveGroup(this);
            Mission?.ExpireMission();

            List<GameBot> temporaryHelpers;

            lock (_groupMembersLock)
            {
                temporaryHelpers = _groupMembers.OfType<GameBot>()
                    .Where(bot => bot.IsTemporaryGroupHelper)
                    .ToList();
                foreach (GameLiving member in _groupMembers)
                {
                    member.Group = null;
                    member.GroupIndex = 0xFF;
                    if (member is GameBot bot)
                    {
                        bot.LeavePlayerLedGroup();
                        if (bot.Brain is DOL.AI.Brain.BotBrain brain)
                            brain.FSM.SetCurrentState(eFSMStateType.IDLE);
                    }
                }
                LivingLeader = null;
                _groupMembers.Clear();
            }

            foreach (GameBot helper in temporaryHelpers)
                helper.Delete();
        }

        private void UpdateGroupIndexes()
        {
            lock (_groupMembersLock)
            {
                for (int i = 0; i < _groupMembers.Count; i++)
                    _groupMembers[i].GroupIndex = (byte) i;
            }
        }

        public bool MakeLeader(GameLiving living)
        {
            GameLiving oldLeader;

            lock (_groupMembersLock)
            {
                if (LivingLeader == living || living == null || living.Group != this)
                    return false;

                byte index = living.GroupIndex;

                oldLeader = _groupMembers[0];
                _groupMembers[index] = oldLeader;
                _groupMembers[0] = living;
                LivingLeader = living;
                living.GroupIndex = 0;
                oldLeader.GroupIndex = index;
            }

            UpdateMembers([oldLeader, living], true, true);
            UpdateGroupWindow();
            SendMessageToGroupMembers($"{Leader.Name} is the new group leader.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return true;
        }

        public bool SwitchPlayers(GameLiving source, GameLiving target)
        {
            lock (_groupMembersLock)
            {
                if (source == target || !_groupMembers.Contains(source) || !_groupMembers.Contains(target))
                    return false;

                byte sourceIndex = source.GroupIndex;
                byte targetIndex = target.GroupIndex;

                source.GroupIndex = targetIndex;
                _groupMembers[targetIndex] = source;
                target.GroupIndex = sourceIndex;
                _groupMembers[sourceIndex] = target;
            }

            UpdateMembers([source, target], true, true);
            UpdateGroupWindow();
            SendMessageToGroupMembers($"Switched group member {source.Name} with {target.Name}", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return true;
        }

        public GamePlayer GetMemberByIndex(byte index)
        {
            GamePlayer player = null;

            lock (_groupMembersLock)
            {
                player = _groupMembers.Count > index ? _groupMembers[index] as GamePlayer : null;
            }

            return player;
        }

        public virtual void SendMessageToGroupMembers(GameLiving from, string msg, eChatType type, eChatLoc loc)
        {
            string message = from != null ? $"[Party] {from.GetName(0, true)}: \"{msg}\"" : $"[Party] {msg}";
            SendMessageToGroupMembers(message, type, loc);
        }

        public virtual void SendMessageToGroupMembers(string msg, eChatType type, eChatLoc loc)
        {
            foreach (GamePlayer player in GetPlayersInTheGroup())
                player.Out.SendMessage(msg, type, loc);
        }

        public void UpdateMember(GameLiving living, bool updateIcons, bool updateOtherRegions)
        {
            UpdateMembers([living], updateIcons, updateOtherRegions);
        }

        public void UpdateMembers(ReadOnlySpan<GameLiving> livings, bool updateIcons, bool updateOtherRegions)
        {
            foreach (GameLiving living in livings)
            {
                if (living.Group != this)
                    continue;

                foreach (GamePlayer player in GetPlayersInTheGroup())
                {
                    if (updateOtherRegions || player.CurrentRegion == living.CurrentRegion)
                        player.Out.SendGroupMemberUpdate(updateIcons, true, living);
                }
            }
        }

        public void UpdateAllToMember(GamePlayer player, bool updateIcons, bool updateOtherRegions)
        {
            if (player.Group != this)
                return;

            foreach (GameLiving living in GetMembersInTheGroup())
            {
                if (updateOtherRegions || living.CurrentRegion == player.CurrentRegion)
                    player.Out.SendGroupMemberUpdate(updateIcons, true, living);
            }
        }

        public void UpdateGroupWindow()
        {
            foreach (GamePlayer player in GetPlayersInTheGroup())
                player.Out.SendGroupWindowUpdate();
        }

        public string Name => $"{(Leader == null || MemberCount <= 0 ? "leaderless" : $"{Leader.Name}'s")} group (size: {MemberCount})";

        public object GameStaticItemOwnerComparand => null;

        public TryPickUpResult TryAutoPickUpMoney(GameMoney money) => SplitCoins(Leader, money);

        public TryPickUpResult TryPickUpMoney(GamePlayer source, GameMoney money) => SplitCoins(source, money);

        /// <summary>
        /// Coins are split across players and gamebots who can hold them. Every copper is paid once.
        /// Companion bots have no purse, so their part stays with the members who do.
        /// </summary>
        private TryPickUpResult SplitCoins(GamePlayer source, GameMoney money)
        {
            money.AssertLockAcquisition();
            if (!AutosplitCoins)
                return TryPickUpResult.DoesNotWant;

            List<GameLiving> fitted = BotsThatFit(EligibleCoinMembers(money), money.Value);
            if (fitted.Count == 0)
            {
                if (source?.Client?.Account != null)
                    source.Out.SendMessage(LanguageMgr.GetTranslation(source.Client.Account.Language, "GamePlayer.PickupObject.NoOneGroupWantsMoney"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return TryPickUpResult.Blocked;
            }

            long[] shares = GroupLoot.SplitCopper(money.Value, fitted.Count);
            long unpaid = 0;
            bool paid = false;
            GamePlayer firstPlayer = null;
            for (int index = 0; index < fitted.Count; index++)
            {
                long share = shares[index];
                if (share <= 0)
                    continue;

                if (fitted[index] is GamePlayer player)
                {
                    firstPlayer ??= player;
                    CreditPlayer(player, share, fitted.Count > 1);
                    paid = true;
                }
                else if (fitted[index] is GameBot bot && AutonomousBotGuildEconomy.PayLootShare(bot, share))
                    paid = true;
                else
                    unpaid += share;
            }

            if (unpaid > 0 && firstPlayer != null)
            {
                CreditPlayer(firstPlayer, unpaid, true);
                unpaid = 0;
            }
            else if (unpaid > 0)
            {
                foreach (GameLiving member in fitted)
                {
                    if (member is GameBot bot && AutonomousBotGuildEconomy.PayLootShare(bot, unpaid))
                    {
                        unpaid = 0;
                        break;
                    }
                }
            }

            // Nothing was stored. Leave the pile where it is so a later pickup can try again.
            if (unpaid > 0 && !paid)
                return TryPickUpResult.Blocked;

            _ = money.RemoveFromWorld();
            return TryPickUpResult.Success;
        }

        private static void CreditPlayer(GamePlayer player, long grossShare, bool shared)
        {
            long net = player.ApplyGuildDues(grossShare);
            if (net <= 0)
                return;

            string language = player.Client?.Account?.Language;
            string message = language == null ? null : LanguageMgr.GetTranslation(language,
                shared ? "GamePlayer.PickupObject.YourLootShare" : "GamePlayer.PickupObject.YouPickUp",
                Money.GetString(grossShare));
            player.AddMoney(net, message);
            InventoryLogging.LogInventoryAction("(ground)", player, eInventoryActionType.Loot, grossShare);
        }

        public TryPickUpResult TryAutoPickUpItem(WorldInventoryItem inventoryItem)
        {
            inventoryItem.AssertLockAcquisition();
            // Discarded gear and a group that is not sharing stay on the solo path.
            // The player's own /autoloot still picks up a kill that has no group.
            // Inside a group with a player, gear is rolled for (need or greed). Everything else
            // is dealt in turn: a player's turn goes to the backpack with /autoloot on, and
            // waits on the ground with it off.
            if (!AutosplitLoot || inventoryItem.IsPlayerDiscarded)
                return TryPickUpResult.DoesNotWant;

            List<GameLiving> eligible = EligibleItemMembers(inventoryItem);
            if (eligible.Count == 0)
                return TryPickUpResult.Blocked;

            // Blocked leaves the drop in the world until the roll ends. DropLoot adds it there.
            if (GroupLootRolls.TryStart(this, inventoryItem, eligible))
                return TryPickUpResult.Blocked;

            lock (_lootTurnLock)
            {
                int count = eligible.Count;
                for (int step = 0; step < count; step++)
                {
                    int index = (_lootTurn + step) % count;
                    GameLiving member = eligible[index];
                    if (member is GamePlayer player)
                    {
                        if (!PlayerCanHold(player, inventoryItem.Item))
                            continue;

                        _lootTurn = index + 1;
                        inventoryItem.ReservedFor = player;
                        if (player.Autoloot && TryPickUpItem(player, inventoryItem) is TryPickUpResult.Success)
                            return TryPickUpResult.Success;
                        if (player.Client != null)
                            player.Out.SendMessage($"{inventoryItem.Name} is yours and waits on the ground. Pick it up, or use /look take.", eChatType.CT_Loot, eChatLoc.CL_SystemWindow);
                        // Blocked leaves the drop in the world. DropLoot adds it there.
                        return TryPickUpResult.Blocked;
                    }

                    if (member is GameBot bot)
                    {
                        TryPickUpResult result = bot.TryAutoPickUpItem(inventoryItem);
                        if (result is not TryPickUpResult.Success)
                            continue;

                        _lootTurn = index + 1;
                        TellGroupItem(inventoryItem.Name, bot.Name);
                        return TryPickUpResult.Success;
                    }
                }
            }

            return TryPickUpResult.Blocked;
        }

        public TryPickUpResult TryPickUpItem(GamePlayer source, WorldInventoryItem item)
        {
            item.AssertLockAcquisition();

            // A group only hands out items while autosplit is on. A player's discarded
            // item stays theirs. The person who looks and takes a drop is the one who receives it.
            if (!AutosplitLoot || item.IsPlayerDiscarded || source == null)
                return TryPickUpResult.DoesNotWant;

            string language = source.Client?.Account?.Language;
            if (item.PendingRoll != null)
            {
                if (language != null)
                    source.Out.SendMessage($"The group is still rolling for {item.Name}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return TryPickUpResult.Blocked;
            }

            if (item.ReservedFor != null && item.ReservedFor != source)
            {
                if (language != null)
                    source.Out.SendMessage($"{item.Name} is {item.ReservedFor.Name}'s to look over.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return TryPickUpResult.Blocked;
            }

            if (!GiveItem(source, item.Item))
            {
                if (language != null)
                    source.Out.SendMessage(LanguageMgr.GetTranslation(language, "GamePlayer.PickupObject.BackpackFull"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return TryPickUpResult.Blocked;
            }

            if (language != null)
            {
                source.Out.SendMessage(LanguageMgr.GetTranslation(language, "GamePlayer.PickupObject.YouGet", item.Item.GetName(1, false)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                if (source.CurrentRegion != null)
                    Message.SystemToOthers(source, LanguageMgr.GetTranslation(language, "GamePlayer.PickupObject.GroupMemberPicksUp", source.Name, item.Item.GetName(1, false)), eChatType.CT_System);
            }

            InventoryLogging.LogInventoryAction("(ground)", source, eInventoryActionType.Loot, item.Item.Template, item.Item.IsStackable ? item.Item.Count : 1);
            item.ReservedFor = null;
            if (!item.RemoveFromWorld())
                item.ObjectState = eObjectState.Inactive;
            return TryPickUpResult.Success;

            static bool GiveItem(GamePlayer player, DbInventoryItem item)
            {
                if (player?.Inventory == null || item == null)
                    return false;

                return item.IsStackable
                    ? player.Inventory.AddTemplate(item, item.Count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                    : player.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item);
            }
        }

        private List<GameLiving> SnapshotMembers()
        {
            List<GameLiving> copy = new();
            lock (_groupMembersLock)
                copy.AddRange(_groupMembers);
            return copy;
        }

        private static bool NearDrop(GameLiving member, GameObject drop) =>
            member != null && drop != null && member.ObjectState == eObjectState.Active &&
            member.CurrentRegionID == drop.CurrentRegionID &&
            member.IsWithinRadius(drop, WorldMgr.VISIBILITY_DISTANCE);

        private List<GameLiving> EligibleCoinMembers(GameMoney money)
        {
            List<GameLiving> members = new();
            foreach (GameLiving member in SnapshotMembers())
            {
                if (!NearDrop(member, money))
                    continue;
                if (member is GamePlayer)
                    members.Add(member);
                else if (member is GameBot bot && bot.IsAutonomousWorldBot && bot.ReceivesGroupLoot &&
                         bot.DatabaseID > 0 && bot.PersistentRecord != null)
                    members.Add(bot);
            }

            return members;
        }

        private List<GameLiving> EligibleItemMembers(WorldInventoryItem item)
        {
            List<GameLiving> members = new();
            foreach (GameLiving member in SnapshotMembers())
            {
                if (!NearDrop(member, item))
                    continue;
                if (member is GamePlayer player)
                {
                    if (player.AutoSplitLoot)
                        members.Add(player);
                }
                else if (member is GameBot bot && bot.ReceivesGroupLoot)
                    members.Add(bot);
            }

            return members;
        }

        /// <summary>
        /// Drops any gamebot whose purse cannot hold its share, then splits again,
        /// so a full purse never deletes the other members' copper.
        /// </summary>
        private static List<GameLiving> BotsThatFit(List<GameLiving> eligible, long total)
        {
            List<GameLiving> fitted = new(eligible);
            for (int pass = 0; pass < eligible.Count && fitted.Count > 0; pass++)
            {
                long[] shares = GroupLoot.SplitCopper(total, fitted.Count);
                int unfit = -1;
                for (int index = 0; index < fitted.Count; index++)
                {
                    if (fitted[index] is GameBot bot && !BotCanHold(bot, shares[index]))
                    {
                        unfit = index;
                        break;
                    }
                }

                if (unfit < 0)
                    return fitted;
                fitted.RemoveAt(unfit);
            }

            return fitted;
        }

        private static bool BotCanHold(GameBot bot, long share)
        {
            if (bot?.PersistentRecord == null || bot.DatabaseID <= 0 || share < 0)
                return false;
            if (share == 0)
                return true;

            // A full guild bank returns the dues to the bot, so the purse must fit the whole share.
            long net = AutonomousBotGuildEconomy.ShareAfterDues(bot, share, out _, out _);
            long worst = Math.Max(net, share);
            return bot.PersistentRecord.MoneyCopper <= long.MaxValue - worst;
        }

        private static bool PlayerCanHold(GamePlayer player, DbInventoryItem item)
        {
            if (player == null)
                return false;
            // A player with no inventory yet still gets the turn; /look reports a full bag later.
            if (player.Inventory == null || item == null)
                return true;
            if (item.IsStackable &&
                player.Inventory.FindFirstPartiallyFullSlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack, item) != eInventorySlot.Invalid)
                return true;
            return player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) != eInventorySlot.Invalid;
        }

        private void TellGroupItem(string itemName, string receiverName)
        {
            foreach (GamePlayer member in SnapshotMembers().OfType<GamePlayer>())
            {
                string language = member.Client?.Account?.Language;
                if (language == null)
                    continue;
                member.Out.SendMessage(LanguageMgr.GetTranslation(language, "GamePlayer.PickupObject.Autosplit", itemName, receiverName), eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
        }

        public bool IsGroupInCombat()
        {
            lock (_groupMembers)
            {
                return _groupMembers.Any(static m => m.InCombat);
            }
        }

        public bool IsInTheGroup(GameLiving living)
        {
            lock (_groupMembers)
            {
                return _groupMembers.Contains(living);
            }
        }

        public string GroupMemberString(GamePlayer player)
        {
            StringBuilder text = new(64);
            BattleGroup battlegroup = player.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY);

            foreach (GamePlayer groupMember in GetPlayersInTheGroup())
            {
                if (battlegroup.IsInTheBattleGroup(groupMember))
                {
                    if (battlegroup.Members.Contains(groupMember))
                        _ = text.Append("<Leader> ");

                    _ = text.Append("(I)");
                }

                _ = text.Append($"{groupMember.Name} ");
            }

            return text.ToString();
        }

        public string GroupMemberClassString()
        {
            StringBuilder text = new(64);

            foreach (GamePlayer groupMember in GetPlayersInTheGroup())
                _ = text.Append($"{groupMember.Name} ({groupMember.CharacterClass.Name}) ");

            return text.ToString();
        }
    }
}
