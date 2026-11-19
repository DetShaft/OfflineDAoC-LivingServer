using System;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>Short-lived reservations scoped to player-versus-player targets only.</summary>
    public static class BotPvpCrowdControl
    {
        private sealed class Claim
        {
            public long Until; public long Start; public int SpellId; public eRealm Realm; public WeakReference<GameBot> Caster;
            public BotCasterSurvival.ControlKind Kind;
        }

        /// <summary>A claim holds without a running cast only this long (the time to start the cast).</summary>
        public const int ClaimStartGraceMilliseconds = 1_000;
        private static readonly ConditionalWeakTable<GameLiving, Claim> Claims = new();
        private sealed class FreeEnemyScan { public long Until; public bool Found; }
        private static readonly ConditionalWeakTable<GameBot, FreeEnemyScan> FreeEnemyScans = new();
        public const ushort FreeEnemyRadius = 1500;
        public static bool PlayerLike(GameLiving living) => living is GamePlayer or GameBot ||
            living is GameNPC npc && npc.Brain is IControlledBrain pet && pet.GetLivingOwner() is IGamePlayer;
        public static bool Protected(GameLiving attacker, GameLiving target)
        {
            if (!PlayerLike(target) || attacker == null || target.Realm == attacker.Realm || target.Realm == eRealm.None) return false;
            GameBot bot = attacker as GameBot;
            if (bot == null && attacker is GameNPC npc && npc.Brain is IControlledBrain pet) bot = pet.GetLivingOwner() as GameBot;
            if (bot == null) return false;
            if (CompanionPvpEngagement.Focused(attacker, target) || CompanionPvpEngagement.Defending(attacker, target)) return false;
            // A real player's explicit attack remains authoritative.
            if (bot.Owner is GamePlayer player && player.IsAttacking && player.TargetObject == target) return false;
            if (target.IsMezzed) return OtherEnemyFree(bot);
            if (!Claims.TryGetValue(target, out Claim claim)) return false;
            lock (claim)
            {
                if (claim.Realm != bot.Realm || claim.Until <= GameLoop.GameLoopTime || claim.Caster == null ||
                    !claim.Caster.TryGetTarget(out GameBot caster) || !caster.IsAlive || caster == bot) return false;
                // #151: a claim that cannot land no longer freezes the group. It ends when the target is
                // immune to that control, or when the claiming cast is no longer running.
                if (claim.Kind != BotCasterSurvival.ControlKind.None && BotCasterSurvival.Immune(target, claim.Kind) ||
                    !ClaimCastAlive(claim, caster))
                {
                    claim.Until = 0;
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// A claim stays alive during the short start grace, then only while its caster is casting the claimed spell.
        /// <paramref name="castingSpellId"/> is null when the caster is not casting.
        /// </summary>
        public static bool ClaimCastAlive(long start, int spellId, long now, int? castingSpellId) =>
            now - start < ClaimStartGraceMilliseconds || spellId != 0 && castingSpellId == spellId;

        private static bool ClaimCastAlive(Claim claim, GameBot caster)
        {
            long now = GameLoop.GameLoopTime;
            if (now - claim.Start < ClaimStartGraceMilliseconds) return true;
            int? casting = caster.castingComponent is { IsCasting: true } component ? component.SpellHandler?.Spell?.ID : null;
            return ClaimCastAlive(claim.Start, claim.SpellId, now, casting);
        }

        // Owner 2026-10-27: a mesmerized enemy is left alone only while another enemy is still free to fight nearby.
        // Once the mez is all that is left, the bots finish it instead of walking away from it.
        // Plain realm checks, not LegalEnemy: that asks the server rules, which ask this class again.
        // One scan a second per bot.
        private static bool OtherEnemyFree(GameBot bot)
        {
            FreeEnemyScan scan = FreeEnemyScans.GetOrCreateValue(bot);
            lock (scan)
            {
                long now = GameLoop.GameLoopTime;
                if (now < scan.Until) return scan.Found;
                scan.Until = now + 1000;
                return scan.Found = bot.GetPlayersInRadius(FreeEnemyRadius).Cast<GameLiving>()
                    .Concat(bot.GetNPCsInRadius(FreeEnemyRadius).Where(PlayerLike))
                    .Any(t => t.IsAlive && t.ObjectState == GameObject.eObjectState.Active &&
                        !t.IsMezzed && !t.IsStealthed && t.Realm != eRealm.None && t.Realm != bot.Realm);
            }
        }

        public static bool Reserve(GameBot caster, GameLiving target, int duration) =>
            Reserve(caster, target, duration, 0, BotCasterSurvival.ControlKind.None);

        public static bool Reserve(GameBot caster, GameLiving target, int duration, int spellId, BotCasterSurvival.ControlKind kind)
        {
            Claim claim = Claims.GetOrCreateValue(target);
            lock (claim)
            {
                if (claim.Until > GameLoop.GameLoopTime && claim.Caster != null && claim.Caster.TryGetTarget(out GameBot old) && old != caster && old.IsAlive &&
                    ClaimCastAlive(claim, old)) return false;
                claim.Realm = caster.Realm;
                claim.Caster = new(caster);
                claim.Start = GameLoop.GameLoopTime;
                claim.SpellId = spellId;
                claim.Kind = kind;
                claim.Until = GameLoop.GameLoopTime + Math.Clamp(duration, 1000, 10_000);
                return true;
            }
        }
        public static void Release(GameBot caster, GameLiving target)
        {
            if (Claims.TryGetValue(target, out Claim claim)) lock (claim)
                if (claim.Caster != null && claim.Caster.TryGetTarget(out GameBot owner) && owner == caster) claim.Until = 0;
        }
    }
}
