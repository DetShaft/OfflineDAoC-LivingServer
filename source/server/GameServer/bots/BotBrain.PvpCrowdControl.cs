using System;
using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        private long _nextPvpControl;
        private int _pvpControlSpellId;
        private long _pvpControlCastUntil;
        private long? _lastPvpAreaMez;
        private bool PvpControlInFlight => Body.IsCasting && GameLoop.GameLoopTime<_pvpControlCastUntil &&
            Body.castingComponent?.SpellHandler?.Spell?.ID==_pvpControlSpellId;
        private bool TryPvpCrowdControl()
        {
            if (BotBody?.CanCastCrowdControlSpells != true || Body.IsCasting || Body.IsIncapacitated ||
                Body.castingComponent?.HasPendingSkillRequests == true || GameLoop.GameLoopTime < _nextPvpControl) return false;
            _nextPvpControl = GameLoop.GameLoopTime + 2000;
            if (!AggroList.Keys.Any(BotPvpCrowdControl.PlayerLike) && !BotPvpCrowdControl.PlayerLike(Body.TargetObject as GameLiving) &&
                Body.Group?.GetMembersInTheGroup().Any(m=>m.IsAttacking && BotPvpCrowdControl.PlayerLike(m.TargetObject as GameLiving))!=true) return false;
            // PvE never enters this policy; native levels, specs, mana, immunity,
            // interruption and spell timers still decide whether a cast succeeds.
            GameLiving[] enemies = Body.GetNPCsInRadius(1800).Where(BotPvpCrowdControl.PlayerLike).Cast<GameLiving>()
                .Concat(Body.GetPlayersInRadius(1800)).Where(t => BotSiegeRuntime.LegalEnemy(Body,t) && !t.IsMezzed &&
                    !t.IsStealthed && !BotPvpCrowdControl.Protected(Body,t) && CompanionEngagementMode.Allows(Body,t) &&
                    !CompanionPvpEngagement.Focused(Body,t) && !CompanionPvpEngagement.Defending(Body,t))
                .OrderBy(Body.GetDistanceTo).Take(24).ToArray();
            if (enemies.Length == 0) return false;
            long now = GameLoop.GameLoopTime;
            foreach (Spell spell in BotPvpCrowdControlPolicy.Order(BotBody.CrowdControlSpells))
            {
                if (spell.Level>Body.Level || Body.GetSkillDisabledDuration(spell)>0 || Body.Mana<BotBody.PowerCost(spell) ||
                    spell.CastTime>0 && Body.IsBeingInterrupted && !spell.Uninterruptible) continue;
                bool area = BotPvpCrowdControlPolicy.IsAreaMez(spell);
                // #152: an area mez is an opener. After an attempt it waits out the 60 s mez immunity it hands out.
                if (area && !BotPvpCrowdControlPolicy.AreaMezReady(now, _lastPvpAreaMez)) continue;
                BotCasterSurvival.ControlKind kind = BotCasterSurvival.KindOf(spell);
                bool breaksOnDamage = BotPvpCrowdControlPolicy.BreaksOnDamage(kind);
                // #152: never aim CC at an enemy that is immune to it, and never mez or root an enemy groupmates are hitting.
                bool Free(GameLiving t) => (kind == BotCasterSurvival.ControlKind.None || !BotCasterSurvival.Immune(t, kind)) &&
                    !(breaksOnDamage && GroupIsHitting(t));
                int range = spell.Target==eSpellTarget.SELF || spell.Range<=0 ? spell.Radius : spell.CalculateEffectiveRange(Body);
                GameLiving[] free = enemies.Where(Free).ToArray();
                int AreaCount(GameLiving centre) => free.Count(n=>n.IsWithinRadius(centre,Math.Max(1,spell.Radius)));
                GameLiving candidate=free.Where(t=>Body.IsWithinRadius(t,range) && !LivingHasEffect(t,spell) &&
                    NeedsOffensiveSpellApplication(t,spell) &&
                    // Point-blank area spells are centred on the caster, targeted ones on the chosen enemy.
                    (!area || BotPvpCrowdControlPolicy.AreaClusterWorthCasting(AreaCount(spell.Target==eSpellTarget.SELF ? Body : t))))
                    .OrderByDescending(t=>AreaCount(t)).ThenBy(Body.GetDistanceTo).FirstOrDefault(t=>BotSiegeRuntime.Visible(Body,t));
                if (candidate==null || !BotPvpCrowdControl.Reserve(BotBody,candidate,spell.CastTime+2000,spell.ID,kind)) continue;
                GameObject previous=Body.TargetObject;
                Body.TargetObject=candidate;
                if (spell.CastTime>0) { Body.StopMovingOnPath(); Body.StopMoving(); }
                bool started=CheckOffensiveSpells(spell);
                if (!started) { BotPvpCrowdControl.Release(BotBody,candidate); Body.TargetObject=previous; continue; }
                _pvpControlSpellId=spell.ID; _pvpControlCastUntil=now+spell.CastTime+2000;
                if (area)
                {
                    _lastPvpAreaMez = now;
                    // Only the free enemies the mez can actually catch are reserved (#151).
                    foreach(GameLiving enemy in free.Where(t=>t.IsWithinRadius(spell.Target==eSpellTarget.SELF ? Body : candidate,spell.Radius)))
                        BotPvpCrowdControl.Reserve(BotBody,enemy,spell.CastTime+2000,spell.ID,kind);
                }
                return true;
            }
            return false;
        }

        /// <summary>True when a groupmate (or a groupmate's pet) is meleeing or casting a harmful spell at this enemy.</summary>
        private bool GroupIsHitting(GameLiving enemy)
        {
            if (enemy == null || Body.Group == null) return false;
            foreach (GameLiving member in Body.Group.GetMembersInTheGroup())
            {
                if (member == null || member == Body || !member.IsAlive) continue;
                if (Hitting(member, enemy) || member.ControlledBrain?.Body is GameNPC pet && Hitting(pet, enemy)) return true;
            }
            return false;
        }

        private static bool Hitting(GameLiving attacker, GameLiving enemy) =>
            attacker.TargetObject == enemy && (attacker.IsAttacking ||
                attacker.IsCasting && attacker.castingComponent?.SpellHandler?.Spell?.IsHarmful == true &&
                attacker.castingComponent.SpellHandler.Spell.SpellType != eSpellType.Mesmerize);
    }
}
