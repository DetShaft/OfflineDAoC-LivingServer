using System;
using System.Collections;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.PropertyCalc;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// Battleground siege the live way (owner, 2026-11-15): the siege merchant sells tokens priced for the battleground's
    /// level bracket. A player hands a token back to him, and a siege engineer follows that player and deploys the
    /// weapon where the player asks. Siegecraft kits still work as before.
    /// </summary>
    public static class BattlegroundSiegeTokens
    {
        /// <summary>The frontier kit price (50 gold) at level 50.</summary>
        public const long FrontierKitPrice = 500_000;
        public const int TokenModel = 485;

        public static readonly BotSiegeKind[] Kinds = { BotSiegeKind.Ram, BotSiegeKind.Ballista, BotSiegeKind.Catapult, BotSiegeKind.Trebuchet };

        public static string TokenId(BotSiegeKind kind, ushort regionId) =>
            $"offline_bg_siege_token_{kind.ToString().ToLowerInvariant()}_{regionId}";

        public static bool TryParse(string templateId, out BotSiegeKind kind, out ushort regionId)
        {
            kind = BotSiegeKind.Ram;
            regionId = 0;
            if (string.IsNullOrEmpty(templateId) || !templateId.StartsWith("offline_bg_siege_token_", StringComparison.Ordinal))
                return false;
            string[] parts = templateId.Substring("offline_bg_siege_token_".Length).Split('_');
            return parts.Length == 2 && Enum.TryParse(parts[0], true, out kind) && ushort.TryParse(parts[1], out regionId);
        }

        /// <summary>
        /// The frontier price scaled by the cube of the bracket's top level over 50: Killaloe (19) 2g 74s, Thidranki (24)
        /// 5g 53s, Murdaigean (29) 9g 76s, Caledonia (35) 17g 15s. Nearest silver, at least 1 silver.
        /// </summary>
        public static long Price(int maxLevel)
        {
            double share = Math.Pow(Math.Clamp(maxLevel, 1, 50) / 50.0, 3);
            long copper = (long)Math.Round(FrontierKitPrice * share / 100.0) * 100;
            return Math.Max(100, copper);
        }

        /// <summary>The engine a spoken or clicked keyword names ("siege ram", "ram", "ballista" ...), any case.</summary>
        public static bool TryKeyword(string text, out BotSiegeKind kind)
        {
            kind = BotSiegeKind.Ram;
            string word = text?.Trim().ToLowerInvariant();
            switch (word)
            {
                case "ram": case "siege ram": kind = BotSiegeKind.Ram; return true;
                case "ballista": case "field ballista": kind = BotSiegeKind.Ballista; return true;
                case "catapult": kind = BotSiegeKind.Catapult; return true;
                case "trebuchet": kind = BotSiegeKind.Trebuchet; return true;
                default: return false;
            }
        }

        public static string DisplayName(BotSiegeKind kind) => kind switch
        {
            BotSiegeKind.Ram => "siege ram",
            BotSiegeKind.Ballista => "field ballista",
            BotSiegeKind.Catapult => "catapult",
            _ => "trebuchet"
        };

        /// <summary>The token's item template for this region, created (or repriced) in the database when needed.</summary>
        public static DbItemTemplate EnsureTemplate(BotSiegeKind kind, ushort regionId, int maxLevel)
        {
            string id = TokenId(kind, regionId);
            long price = Price(maxLevel);
            DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(id);
            if (template == null)
            {
                template = new DbItemTemplate
                {
                    Id_nb = id, Name = $"{DisplayName(kind)} token", Level = 1, Model = TokenModel, Object_Type = 0, Item_Type = 40,
                    Price = price, PackSize = 1, MaxCount = 1, Weight = 1, IsPickable = true, IsDropable = true, IsTradable = true
                };
                GameServer.Database.AddObject(template);
            }
            else if (template.Price != price)
            {
                template.Price = price;
                GameServer.Database.SaveObject(template);
            }
            return template;
        }
    }

    /// <summary>A battleground siege merchant: sells siege tokens and sends a siege engineer with whoever hands one in.</summary>
    public class GameBattlegroundSiegeMerchant : GameMerchant
    {
        public override void LoadFromDatabase(DataObject merchantobject)
        {
            base.LoadFromDatabase(merchantobject);
            if (merchantobject is not DbMob mob)
                return;
            int maxLevel = BattlegroundBrackets.ForRegion(mob.Region)?.MaxLevel ?? 50;
            // The list keeps its id (SiegeMerchantAlb etc.) for the "Siege Equipment" label; it shows the tokens only.
            var tokens = new TokenTradeItems(TradeItems?.ItemsListID ?? "BattlegroundSiegeTokens");
            int slot = 0;
            foreach (BotSiegeKind kind in BattlegroundSiegeTokens.Kinds)
                tokens.AddTradeItem(0, (eMerchantWindowSlot)slot++, BattlegroundSiegeTokens.EnsureTemplate(kind, mob.Region, maxLevel));
            TradeItems = tokens;
        }

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            SayTo(player, "Buy a token for the engine you want, then hand the token back to me. One of my engineers will follow " +
                "you and set the engine up wherever you tell him to. Carrying a token already? Just tell me which: " +
                "[siege ram], [ballista], [catapult] or [trebuchet].");
            return true;
        }

        /// <summary>
        /// Saying the engine's name hands that token in from the backpack (#115: dragging the token onto the Siegemaster
        /// failed when the player stood more than the 128-unit give distance away, or the drop missed him).
        /// </summary>
        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text))
                return false;
            if (source is not GamePlayer player || !BattlegroundSiegeTokens.TryKeyword(text, out BotSiegeKind kind))
                return true;
            string id = BattlegroundSiegeTokens.TokenId(kind, CurrentRegionID);
            DbInventoryItem token = player.Inventory
                .GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                .FirstOrDefault(i => i != null && i.Id_nb == id);
            if (token == null)
            {
                SayTo(player, $"You have no {BattlegroundSiegeTokens.DisplayName(kind)} token for this battleground. Buy one from me first.");
                return true;
            }
            HandIn(player, token, kind, CurrentRegionID);
            return true;
        }

        public override bool ReceiveItem(GameLiving source, DbInventoryItem item)
        {
            if (source is not GamePlayer player || item == null ||
                !BattlegroundSiegeTokens.TryParse(item.Id_nb, out BotSiegeKind kind, out ushort regionId))
                return base.ReceiveItem(source, item);
            return HandIn(player, item, kind, regionId);
        }

        private bool HandIn(GamePlayer player, DbInventoryItem item, BotSiegeKind kind, ushort regionId)
        {
            if (!IsWithinRadius(player, WorldMgr.INTERACT_DISTANCE))
            {
                player.Out.SendMessage($"You are too far away to give anything to {GetName(0, false)}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return false;
            }
            if (regionId != CurrentRegionID || player.Realm != Realm && Realm != eRealm.None)
            {
                SayTo(player, "That token is no good here.");
                return false;
            }
            if (GameSiegeEngineer.HasEngineer(player))
            {
                SayTo(player, "One of my engineers is already with you. Have him deploy his engine first.");
                return false;
            }

            DbItemTemplate template = item.Template;
            if (!player.Inventory.RemoveCountFromStack(item, 1))
                return false;

            var engineer = new GameSiegeEngineer(player, kind, template, this);
            if (!engineer.AddToWorld())
            {
                player.Inventory.AddTemplate(GameInventoryItem.Create(template), 1, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                return false;
            }
            SayTo(player, $"Very well. {engineer.Name} will follow you with a {BattlegroundSiegeTokens.DisplayName(kind)}. " +
                "Talk to him when you want it set up.");
            return true;
        }

        /// <summary>Shows only the tokens (and items added by script, such as the bots' repair kits), not the list's frontier kits.</summary>
        private sealed class TokenTradeItems : MerchantTradeItems
        {
            public TokenTradeItems(string itemsListId) : base(itemsListId) { }

            public override IDictionary GetItemsInPage(int page)
            {
                var items = new Hashtable();
                foreach (DictionaryEntry entry in m_usedItemsTemplates)
                {
                    int key = (int)entry.Key;
                    if (key / MAX_ITEM_IN_TRADEWINDOWS == page)
                        items[key % MAX_ITEM_IN_TRADEWINDOWS] = entry.Value;
                }
                return items;
            }

            public override DbItemTemplate GetItem(int page, eMerchantWindowSlot slot)
            {
                slot = GetValidSlot(page, slot);
                if (slot == eMerchantWindowSlot.Invalid)
                    return null;
                return m_usedItemsTemplates[page * MAX_ITEM_IN_TRADEWINDOWS + (int)slot] as DbItemTemplate;
            }

            public override IDictionary GetAllItems()
            {
                var items = new Hashtable();
                foreach (DictionaryEntry entry in m_usedItemsTemplates)
                    items[(int)entry.Key] = entry.Value;
                return items;
            }
        }
    }

    /// <summary>
    /// Where and how fast a siege engineer moves (#148). Plain numbers, so the rules can be tested without a world.
    /// </summary>
    public static class SiegeEngineerPlan
    {
        /// <summary>A player's base run speed (<see cref="GamePlayer.PLAYER_BASE_SPEED"/>) at the top speed buff rate.</summary>
        public static readonly short RealmSpeed = (short)Math.Round(GamePlayer.PLAYER_BASE_SPEED * MaxSpeedCalculator.SPEED5);

        /// <summary>The ram spell's band: the player must stand 200 to 500 units from the door (SummonSiegeRam).</summary>
        public const int RamMinRange = 200;
        public const int RamMaxRange = 500;

        /// <summary>Where the engineer waits to build a ram: inside the band with a margin on both sides.</summary>
        public const int RamStagingRange = 320;

        /// <summary>The player counts as beside the engineer within this distance.</summary>
        public const int BesideRange = 150;

        /// <summary>How long the engineer waits at the door for the player before he goes back to following.</summary>
        public const int StagingTimeoutMs = 90_000;

        /// <summary>
        /// The engineer's permanent speed: realm speed, or a third faster than the player if the player is faster still
        /// (mounted, sprinting under a speed buff), so he never falls behind.
        /// </summary>
        public static short Speed(int ownerSpeed) =>
            (short)Math.Max(RealmSpeed, Math.Min(short.MaxValue, Math.Ceiling(Math.Max(0, ownerSpeed) * 1.3)));

        public static bool InRamBand(double distanceToDoor) => distanceToDoor >= RamMinRange && distanceToDoor <= RamMaxRange;

        /// <summary>
        /// The ram staging point: <see cref="RamStagingRange"/> out from the door, on the side the player stands (the
        /// outside of the keep). With the player on top of the door, the side the engineer comes from is used instead.
        /// </summary>
        public static (int X, int Y) RamStagingPoint(int doorX, int doorY, int ownerX, int ownerY, int engineerX, int engineerY)
        {
            double dx = ownerX - doorX, dy = ownerY - doorY;
            if (dx * dx + dy * dy < 40 * 40)
            {
                dx = engineerX - doorX;
                dy = engineerY - doorY;
            }
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1)
                return (doorX + RamStagingRange, doorY);
            return ((int)Math.Round(doorX + dx / length * RamStagingRange), (int)Math.Round(doorY + dy / length * RamStagingRange));
        }
    }

    /// <summary>
    /// Follows the player who handed in a siege token and deploys that engine on request. The engine is set up as if
    /// the player used the siegecraft kit (same spell, so the same rules: a ram needs a targeted door in range). He goes
    /// home after 20 minutes, or when the player leaves the region, and the unused token goes back to the player.
    /// #148: he has no monster brain (the standard one walked him back to the Siegemaster, where he was "spawned",
    /// between follow checks, so he wandered and hung back), he keeps realm speed or better, and for a ram he walks to
    /// the targeted door himself and builds it once the player joins him there.
    /// </summary>
    public class GameSiegeEngineer : GameNPC
    {
        public const int IdleMinutes = 20;
        public const int FollowDistance = 120;
        private const int FollowMax = 30_000;                // squared in the follow check, so it must stay well inside int
        private const int WatchMs = 500;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<GamePlayer, GameSiegeEngineer> Active = new();

        private readonly GamePlayer _owner;
        private readonly BotSiegeKind _kind;
        private readonly DbItemTemplate _token;
        private readonly long _expires;
        private ECSGameTimer _watch;
        private bool _done;

        // A ram on its way to a door: the door, where he waits, and when he stops waiting.
        private GameLiving _door;
        private Point3D _staging;
        private long _stagingUntil;
        private long _stagingStarted;
        private bool _toldArrived;

        public static bool HasEngineer(GamePlayer player) =>
            player != null && Active.TryGetValue(player, out GameSiegeEngineer engineer) && engineer.ObjectState == eObjectState.Active;

        public GameSiegeEngineer(GamePlayer owner, BotSiegeKind kind, DbItemTemplate token, GameNPC merchant) : base(new DOL.AI.Brain.BlankBrain())
        {
            _owner = owner;
            _kind = kind;
            _token = token;
            _expires = GameLoop.GameLoopTime + IdleMinutes * 60_000L;
            Name = "siege engineer";
            GuildName = $"{owner.Name}'s {BattlegroundSiegeTokens.DisplayName(kind)}";
            Model = merchant.Model;
            Size = merchant.Size;
            Level = merchant.Level;
            Realm = merchant.Realm;
            Flags |= eFlags.PEACE;
            MaxSpeedBase = SiegeEngineerPlan.RealmSpeed;
            movementComponent.FixedSpeed = true;          // MaxSpeedBase as set, not lowered by health or anything else
            CurrentRegion = merchant.CurrentRegion;
            X = merchant.X;
            Y = merchant.Y;
            Z = merchant.Z;
            Heading = merchant.Heading;
        }

        public bool IsGoingToDoor => _door != null;

        public override bool AddToWorld()
        {
            if (!base.AddToWorld())
                return false;
            Active[_owner] = this;
            KeepPace();
            Follow(_owner, FollowDistance, FollowMax);
            _watch = new ECSGameTimer(this, Watch, WatchMs);
            return true;
        }

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            TurnTo(player, 5000);
            if (player != _owner)
            {
                SayTo(player, $"I'm working for {_owner.Name}.");
                return true;
            }
            string ramNote = _kind == BotSiegeKind.Ram
                ? " For the ram, target the door you want rammed first: I'll walk up to it and build it when you're beside me."
                : "";
            SayTo(player, $"I've got the parts for a {BattlegroundSiegeTokens.DisplayName(_kind)}. Tell me to [deploy] it.{ramNote} " +
                "If you've changed your mind, I can [return] your token.");
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text) || source != _owner || _done)
                return false;
            if (text.Equals("deploy", StringComparison.OrdinalIgnoreCase))
            {
                if (_kind == BotSiegeKind.Ram)
                    StartRam();
                else if (Deploy())
                    Finish(refund: false);
                return true;
            }
            if (text.Equals("return", StringComparison.OrdinalIgnoreCase))
            {
                Finish(refund: true);
                return true;
            }
            return true;
        }

        /// <summary>A ram: build it here if the player already stands in range of the targeted door, else walk to the door.</summary>
        private void StartRam()
        {
            if (_owner.TargetObject is not (Keeps.GameKeepDoor or Keeps.GameRelicDoor) || _owner.TargetObject is not GameLiving door)
            {
                SayTo(_owner, "Target the door you want rammed, then tell me to [deploy].");
                return;
            }
            if (!door.IsAlive)
            {
                SayTo(_owner, "That door is already down.");
                return;
            }
            if (SiegeEngineerPlan.InRamBand(_owner.GetDistanceTo(door)) && IsWithinRadius(_owner, 400))
            {
                if (Deploy())
                    Finish(refund: false);
                return;
            }

            (int x, int y) = SiegeEngineerPlan.RamStagingPoint(door.X, door.Y, _owner.X, _owner.Y, X, Y);
            _door = door;
            _staging = new Point3D(x, y, _owner.GetDistanceTo(door) < SiegeEngineerPlan.RamMaxRange * 2 ? _owner.Z : door.Z);
            _stagingUntil = GameLoop.GameLoopTime + SiegeEngineerPlan.StagingTimeoutMs;
            _toldArrived = false;
            _stagingStarted = GameLoop.GameLoopTime;
            StopFollowing();
            KeepPace();
            PathTo(_staging, MaxSpeedBase);
            SayTo(_owner, $"Right, I'll set it up at the {door.Name}. Meet me there and keep the door targeted.");
        }

        /// <summary>Casts the realm's siegecraft kit spell as the owner, so the engine follows the kit's placement rules.</summary>
        private bool Deploy()
        {
            if (!IsWithinRadius(_owner, 400))
            {
                SayTo(_owner, "Wait for me to catch up.");
                return false;
            }
            DbItemTemplate kit = GameServer.Database.FindObjectByKey<DbItemTemplate>(BotSiegeRuntime.Kit(_owner.Realm, _kind));
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Item_Effects);
            Spell spell = kit == null || line == null ? null : SkillBase.FindSpell(kit.SpellID, line);
            if (spell == null || ScriptMgr.CreateSpellHandler(_owner, spell, line) is not SpellHandler handler)
            {
                SayTo(_owner, "I can't find the plans for that engine.");
                return false;
            }
            if (!handler.StartSpell(_owner))
                return false;                              // the spell tells the player why (wrong target, too close...)
            SayTo(_owner, "There you go. Good luck.");
            return true;
        }

        /// <summary>Realm speed, or faster than the player when the player is faster (#148).</summary>
        private void KeepPace()
        {
            short speed = SiegeEngineerPlan.Speed(_owner.movementComponent?.MaxSpeed ?? GamePlayer.PLAYER_BASE_SPEED);
            if (MaxSpeedBase != speed)
                MaxSpeedBase = speed;
        }

        private int Watch(ECSGameTimer timer)
        {
            if (_done)
                return 0;
            bool ownerGone = _owner.ObjectState != eObjectState.Active || _owner.CurrentRegion != CurrentRegion ||
                _owner.Client?.IsPlaying != true;
            if (ownerGone || GameLoop.GameLoopTime > _expires)
            {
                Finish(refund: true);
                return 0;
            }
            KeepPace();
            // Nothing should ever send him "home", but if anything does, home is where he stands.
            SpawnPoint = new Point3D(X, Y, Z);

            if (_door != null)
            {
                WatchRam();
                return timer.Interval;
            }

            if (!IsWithinRadius(_owner, 3000))
                MoveTo(_owner.CurrentRegionID, _owner.X, _owner.Y, _owner.Z, Heading);   // left behind (a port, a jump): catch up
            if (FollowTarget != _owner)
                Follow(_owner, FollowDistance, FollowMax);
            return timer.Interval;
        }

        private void WatchRam()
        {
            GameLiving door = _door;
            if (!door.IsAlive || door.ObjectState != eObjectState.Active || GameLoop.GameLoopTime > _stagingUntil)
            {
                SayTo(_owner, door.IsAlive ? "You didn't come, so I'm back with you. Tell me to [deploy] again at the door." :
                    "The door's down already. I'm with you again.");
                StopRam();
                return;
            }
            // Arrived, or stopped as close as the mesh lets him (after a moment, so a path being planned is not taken as a stop).
            bool arrived = IsWithinRadius(_staging, 60) ||
                !IsMoving && GameLoop.GameLoopTime - _stagingStarted > 3_000 && IsWithinRadius(_staging, 250);
            if (!arrived)
            {
                if (!IsMoving)
                    PathTo(_staging, MaxSpeedBase);
                return;
            }
            TurnTo(door);
            if (!_toldArrived)
            {
                _toldArrived = true;
                SayTo(_owner, "I'm at the door. Come stand beside me with the door targeted.");
            }
            if (!IsWithinRadius(_owner, SiegeEngineerPlan.BesideRange) || !SiegeEngineerPlan.InRamBand(_owner.GetDistanceTo(door)))
                return;
            if (_owner.TargetObject != door)
                return;                                    // the "keep it targeted" line above covers it; the ram spell needs the target
            if (Deploy())
            {
                Finish(refund: false);
                return;
            }
            // One try only: the spell already told the player why (too close, too many rams...). Retrying every check
            // repeated that message twice a second (review on #158).
            SayTo(_owner, "It won't go up here. I'm back with you; tell me to [deploy] again when you're ready.");
            StopRam();
        }

        private void StopRam()
        {
            _door = null;
            _toldArrived = false;
            Follow(_owner, FollowDistance, FollowMax);
        }

        private void Finish(bool refund)
        {
            if (_done)
                return;
            _done = true;
            if (refund && _token != null && _owner.Inventory != null &&
                _owner.Inventory.AddTemplate(GameInventoryItem.Create(_token), 1, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                _owner.Out.SendMessage($"The siege engineer gives your {_token.Name} back.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            Active.TryRemove(new System.Collections.Generic.KeyValuePair<GamePlayer, GameSiegeEngineer>(_owner, this));
            _watch?.Stop();
            StopFollowing();
            Delete();
        }
    }
}
