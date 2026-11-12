using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>One native song source at a time; never extend its child effects or skip recast timers.</summary>
    public static class BotSongTwistPolicy
    {
        public readonly record struct Song(int Id, int CastMilliseconds, int CooldownMilliseconds, long RemainingMilliseconds);

        public static bool IsManagedSong(GameLiving living, Spell spell)
        {
            if (living is not GameBot bot || bot.CharacterClass == null ||
                spell == null || !spell.IsPulsing || spell.IsHarmful || spell.IsFocus)
                return false;
            eCharacterClass characterClass = (eCharacterClass)bot.CharacterClass.ID;
            if (BotBrain.IsClassicSongClass(characterClass))
                return BotBrain.IsMaintainableClassBuff(spell);
            // Tank chants use a separate selector: never send Paladin/Warden
            // through the performer code that drops songs for solo melee.
            return spell.IsInstantCast && !spell.NeedInstrument && (characterClass switch
            {
                eCharacterClass.Paladin => spell.SpellType is eSpellType.EnduranceRegenBuff or
                    eSpellType.SpecArmorFactorBuff or eSpellType.DamageAdd or eSpellType.CombatHeal,
                eCharacterClass.Warden => spell.SpellType is eSpellType.SpeedEnhancement or
                    eSpellType.Bladeturn or eSpellType.DamageAdd,
                _ => false
            });
        }

        /// <summary>The one chant a solo Paladin holds: damage in a fight, the combat heal when hurt, endurance otherwise.</summary>
        public static eSpellType SoloPaladinAnchor(bool fighting, int healthPercent) =>
            !fighting ? eSpellType.EnduranceRegenBuff : healthPercent < 70 ? eSpellType.CombatHeal : eSpellType.DamageAdd;

        public static bool IsMobileSong(GameLiving living, Spell spell)
        {
            return IsManagedSong(living, spell) &&
                (spell.IsInstantCast || spell.NeedInstrument || spell.MoveCast);
        }

        // Only temporary player companions use this travel pool. Bards have
        // a real endurance song; Skalds and Minstrels do not, so their useful
        // second travel song is health regeneration. Power and combat chants
        // must not take the speed anchor while the owner is moving.
        public static bool IsCompanionTravelSong(eCharacterClass characterClass, eSpellType type) =>
            characterClass switch
            {
                eCharacterClass.Bard => type is eSpellType.SpeedEnhancement or eSpellType.EnduranceRegenBuff,
                eCharacterClass.Skald or eCharacterClass.Minstrel =>
                    type is eSpellType.SpeedEnhancement or eSpellType.HealthRegenBuff,
                _ => false
            };

        // Only one helpful pulse source is legal. Generic buff selection must
        // not independently replace a tank's anchor with a resist/endurance
        // pulse; those are deliberately not part of the chosen core rotation.
        public static bool IsReservedPulse(GameLiving living, Spell spell) =>
            IsManagedSong(living, spell) ||
            living is GameBot bot && bot.CharacterClass != null &&
            (eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Paladin or eCharacterClass.Warden &&
            spell != null && spell.IsPulsing && !spell.IsHarmful && !spell.IsFocus &&
            BotBrain.IsMaintainableClassBuff(spell);

        public static bool HasMobileSongCast(GameLiving living)
        {
            if (living is not GameBot bot || bot.CharacterClass == null)
                return false;

            CastingComponent casting = living.castingComponent;
            if (casting == null) return false;
            Spell active = casting.SpellHandler?.Spell;
            Spell queued = casting.QueuedSpellHandler?.Spell;
            bool pending = casting.TryPeekPendingSpell(out Spell requested);
            // Never use a song to authorize movement during an ordinary heal,
            // hostile spell, or unknown ability request queued alongside it.
            return (active != null || queued != null || pending) &&
                (active == null || IsMobileSong(living, active)) &&
                (queued == null || IsMobileSong(living, queued)) &&
                (!pending || IsMobileSong(living, requested));
        }

        /// <summary>
        /// The one chant a grouped Paladin holds: endurance out of combat, the combat heal while someone
        /// nearby is hurt in a fight, damage otherwise. Grouped Paladins used to twist three or four chants
        /// (see Choose), which recast a chant every second or two, while moving too (issue #32).
        /// </summary>
        public static eSpellType GroupPaladinAnchor(bool fighting, bool groupInjured) =>
            !fighting ? eSpellType.EnduranceRegenBuff : groupInjured ? eSpellType.CombatHeal : eSpellType.DamageAdd;

        /// <summary>How long a Paladin keeps a chant before it may switch to another one.</summary>
        public const int PaladinChantHoldMilliseconds = 10_000;

        /// <summary>
        /// A chant this brain cast this recently is never cast again. Casting a chant that is already
        /// running turns it off (CastingComponent.StartSkill), so a recast while its pulse effect is not
        /// yet visible would toggle it off and on every think.
        /// </summary>
        public const int TankChantRecastGuardMilliseconds = 6_000;

        /// <summary>
        /// Whether a tank chant upkeep pass may cast chosenId now.
        /// lastCastId is 0 when this brain has not cast a chant yet.
        /// holdMilliseconds and keepWhileMoving apply only when another chant is already running:
        /// nothing running always starts the chosen chant.
        /// </summary>
        public static bool MayStartTankChant(int chosenId, int activeId, int lastCastId, long lastCastTick,
            long now, bool moving, bool keepWhileMoving, int holdMilliseconds)
        {
            if (chosenId == 0 || chosenId == activeId)
                return false;
            if (lastCastId != 0 && chosenId == lastCastId && now - lastCastTick < TankChantRecastGuardMilliseconds)
                return false;
            if (activeId == 0)
                return true;
            if (keepWhileMoving && moving)
                return false;
            return lastCastId == 0 || now - lastCastTick >= holdMilliseconds;
        }

        public static eSpellType WardenAnchor(bool traveling, bool combat, bool grouped, bool hasBladeturn)
        {
            if (traveling && !combat) return eSpellType.SpeedEnhancement;
            if (hasBladeturn && (combat || grouped)) return eSpellType.Bladeturn;
            return eSpellType.DamageAdd;
        }

        public static int Choose(int primaryId, int activeId, IReadOnlyList<Song> songs)
        {
            Song primary = songs.FirstOrDefault(song => song.Id == primaryId);
            if (primary.Id == 0) return 0;
            // Return to the anchor immediately after the secondary takes. In
            // particular Skald must not leave speed while its 8s reuse is up.
            if (activeId != primaryId)
                return primary.CooldownMilliseconds <= 0 ? primaryId : 0;
            if (primary.CooldownMilliseconds > 0) return 0;

            foreach (Song secondary in songs.Where(song => song.Id != primaryId && song.CooldownMilliseconds <= 0)
                         .OrderBy(song => song.RemainingMilliseconds))
            {
                int roundTrip = Math.Max(0, secondary.CastMilliseconds) + Math.Max(0, primary.CastMilliseconds) + 1000;
                if (primary.RemainingMilliseconds > roundTrip && secondary.RemainingMilliseconds <= roundTrip)
                    return secondary.Id;
            }
            return 0;
        }
    }
}
