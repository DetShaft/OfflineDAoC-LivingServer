using System;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.Styles;

namespace DOL.GS
{
    /// <summary>What a caster does this turn about a player-shaped enemy in melee reach (issue #136).</summary>
    public enum BotKiteAction
    {
        /// <summary>No threat: the ordinary turn.</summary>
        None,
        /// <summary>An instant stun, mez, root or snare on the attacker.</summary>
        InstantControl,
        /// <summary>Quickcast a control spell that has a cast time.</summary>
        QuickcastControl,
        /// <summary>Sprint away from the attacker, then turn and cast.</summary>
        Run,
        /// <summary>The attacker is held and the caster cannot run: cast from where it stands, no melee.</summary>
        HoldAndCast,
        /// <summary>Quickcast a nuke: no control and nowhere to run.</summary>
        QuickcastNuke,
        /// <summary>Last resort: the old melee fallback.</summary>
        Melee,
    }

    /// <summary>
    /// Issue #136 (Jay, 2026-11-18, after fighting Hibernian Enchanters in 1.1.20): a caster in melee range swapped to
    /// its staff. In DAoC that is a death sentence, so casters kite: control the attacker (instant stun/mez/root/snare,
    /// or Quickcast one), sprint away, turn and cast again. Melee is the last resort. Pets peel for their owner, and pure
    /// melee and shield users peel for their group's healers first, then its casters.
    ///
    /// Scope: every bot kind (autonomous gamebots of all three realms, bots in a player's group, /spawn helpers), and
    /// only against player-shaped attackers (players, bots and their pets; <see cref="BotPvpCrowdControl.PlayerLike"/>).
    /// Fights with mobs are unchanged. The class lists below come from the class's role, not its realm, and every
    /// spell and ability is the bot's own trained one, so each realm's casters get exactly what their spell lines give.
    /// </summary>
    public static class BotCasterSurvival
    {
        /// <summary>A player-shaped enemy this far past the caster's melee range, targeting it, is a melee threat.</summary>
        public const int ThreatReachPadding = 150;
        /// <summary>How far one kite leg runs from the attacker.</summary>
        public const int LegDistance = 550;
        public const int LegMaxMilliseconds = 2_500;
        /// <summary>A leg ends early once the attacker is this far past melee range.</summary>
        public const int SafeGap = 450;
        /// <summary>Legs from an attacker that is not held count toward this budget; then the caster stands.</summary>
        public const int MaxLegsPerWindow = 3;
        public const int LegWindowMilliseconds = 20_000;
        /// <summary>A caster slowed below this fraction of its own speed cannot outrun anyone.</summary>
        public const double SnaredSpeedFraction = 0.6;
        /// <summary>An attacker below this fraction of the caster's speed is held for kiting.</summary>
        public const double HeldSpeedFraction = 0.75;
        /// <summary>A snare counts as kiting control from this much slow on.</summary>
        public const int MinSnareValue = 30;

        // ---- pure rules

        public static BotKiteAction Decide(bool threat, bool threatHeld, bool instantControl, bool quickcastControl,
            bool canRun, bool legsLeft, bool quickcastNuke)
        {
            if (!threat)
                return BotKiteAction.None;
            if (!threatHeld && instantControl)
                return BotKiteAction.InstantControl;
            if (!threatHeld && quickcastControl)
                return BotKiteAction.QuickcastControl;
            if (canRun && (threatHeld || legsLeft))
                return BotKiteAction.Run;
            if (threatHeld)
                return BotKiteAction.HoldAndCast;
            if (quickcastNuke)
                return BotKiteAction.QuickcastNuke;
            return BotKiteAction.Melee;
        }

        public enum ControlKind { None, Stun, Mez, Root, Snare }

        /// <summary>Spells that stop or slow a melee attacker, best first (stun, mez, root, snare).</summary>
        public static ControlKind KindOf(Spell spell)
        {
            if (spell == null || !spell.IsHarmful)
                return ControlKind.None;
            return spell.SpellType switch
            {
                eSpellType.Stun or eSpellType.UnresistableStun or eSpellType.UnrresistableNonImunityStun => ControlKind.Stun,
                eSpellType.Mesmerize => ControlKind.Mez,
                eSpellType.SpeedDecrease or eSpellType.UnbreakableSpeedDecrease or eSpellType.DamageSpeedDecrease =>
                    spell.Value >= 99 ? ControlKind.Root : spell.Value >= MinSnareValue ? ControlKind.Snare : ControlKind.None,
                _ => ControlKind.None,
            };
        }

        /// <summary>Kite legs still allowed: those started in the last window count.</summary>
        public static bool LegsLeft(long[] legStarts, long now)
        {
            int recent = 0;
            foreach (long start in legStarts)
                if (start > 0 && now - start < LegWindowMilliseconds)
                    recent++;
            return recent < MaxLegsPerWindow;
        }

        /// <summary>A point <paramref name="distance"/> from the bot, straight away from the attacker, turned by <paramref name="turnDegrees"/>.</summary>
        public static (int X, int Y) AwayPoint(int botX, int botY, int attackerX, int attackerY, int distance, double turnDegrees)
        {
            double dx = botX - attackerX, dy = botY - attackerY;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double angle = length < 1 ? 0 : Math.Atan2(dy, dx);
            angle += turnDegrees * Math.PI / 180;
            return ((int)Math.Round(botX + Math.Cos(angle) * distance), (int)Math.Round(botY + Math.Sin(angle) * distance));
        }

        /// <summary>Straight away first, then fanning out, for a leg the navmesh can take.</summary>
        public static readonly double[] LegTurns = [0, 30, -30, 60, -60, 90, -90];

        /// <summary>Archers keep their own bow policy (BotRangedCombat).</summary>
        public static bool IsArcher(eCharacterClass characterClass) =>
            characterClass is eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger;

        /// <summary>
        /// Who peels: the pure melee (tanks and heavy melee) and anyone fighting with a shield, since shield styles carry
        /// stuns. Casters, back-line supports and archers do not.
        /// </summary>
        public static bool IsPeeler(eCharacterClass characterClass, bool shieldEquipped, bool rangedCaster) =>
            !rangedCaster && !BotPartyRoles.HoldsBackLine(characterClass) && !IsArcher(characterClass) &&
            (BotPartyRoles.For(characterClass) == BotPartyRole.Tank || BotPartyRoles.CanOffTank(characterClass) || shieldEquipped);

        /// <summary>Healers are peeled for first, then casters. Anyone else (-1) is left to their own fight.</summary>
        public static int PeelPriority(bool healer, bool caster) => healer ? 0 : caster ? 1 : -1;

        public static bool IsPeelHealer(eCharacterClass characterClass) => BotPartyRoles.HoldsBackLine(characterClass);

        public static bool IsPeelCaster(ICharacterClass characterClass) => BotBrain.PrefersSpellRange(characterClass, true, true);

        // ---- live reads

        public static bool ShieldEquipped(GameBot bot) =>
            bot?.Inventory?.GetItem(eInventorySlot.LeftHandWeapon) is { Object_Type: (int)eObjectType.Shield } &&
            bot.ActiveWeaponSlot == eActiveWeaponSlot.Standard;

        public static bool Immune(GameLiving target, ControlKind kind)
        {
            EffectListComponent effects = target?.effectListComponent;
            if (effects == null)
                return false;
            bool npc = target is GameNPC and not GameBot;
            return kind switch
            {
                ControlKind.Stun => effects.ContainsEffectForEffectType(npc ? eEffect.NPCStunImmunity : eEffect.StunImmunity),
                ControlKind.Mez => effects.ContainsEffectForEffectType(npc ? eEffect.NPCMezImmunity : eEffect.MezImmunity),
                ControlKind.Root or ControlKind.Snare => effects.ContainsEffectForEffectType(eEffect.SnareImmunity),
                _ => true,
            };
        }

        /// <summary>The attacker cannot reach the caster for now: stunned, mezzed, rooted or well slowed.</summary>
        public static bool Held(GameLiving attacker, GameLiving bot) =>
            attacker.IsStunned || attacker.IsMezzed ||
            attacker.MaxSpeed < Math.Max(1, (int)bot.MaxSpeedBase) * HeldSpeedFraction;

        /// <summary>The caster itself is rooted or slowed too far to outrun anyone.</summary>
        public static bool CanRun(GameBot bot) =>
            !bot.IsStunned && !bot.IsMezzed && bot.MaxSpeedBase > 0 &&
            bot.MaxSpeed >= bot.MaxSpeedBase * SnaredSpeedFraction;

        // ---- one peeler per attacker

        private sealed class PeelClaim { public long Until; public WeakReference<GameBot> Peeler; }
        private static readonly ConditionalWeakTable<GameLiving, PeelClaim> PeelClaims = new();
        public const int PeelClaimMilliseconds = 4_000;

        /// <summary>Claims the attacker for this peeler; false while another live peeler of the same group holds it.</summary>
        public static bool ClaimPeel(GameBot peeler, GameLiving attacker)
        {
            PeelClaim claim = PeelClaims.GetOrCreateValue(attacker);
            lock (claim)
            {
                long now = GameLoop.GameLoopTime;
                if (claim.Until > now && claim.Peeler != null && claim.Peeler.TryGetTarget(out GameBot other) &&
                    other != peeler && other.IsAlive && other.Group != null && other.Group == peeler.Group)
                    return false;
                claim.Peeler = new(peeler);
                claim.Until = now + PeelClaimMilliseconds;
                return true;
            }
        }

        /// <summary>When peeling, a shield or other style with a stun the target is not immune to goes first.</summary>
        public static Style PeelStunStyle(GameBot bot, AttackData lastAttack)
        {
            if (bot?.Styles == null || bot.TargetObject is not GameLiving target || Immune(target, ControlKind.Stun) ||
                target.IsStunned)
                return null;
            Style best = null;
            foreach (Style style in bot.Styles)
            {
                if (style?.Procs == null || !style.Procs.Any(p => p.Spell?.SpellType is eSpellType.StyleStun) ||
                    !BotMeleeStylePolicy.Usable(bot, style, lastAttack))
                    continue;
                if (BotMeleeStylePolicy.Better(style, best))
                    best = style;
            }
            return best;
        }
    }
}
