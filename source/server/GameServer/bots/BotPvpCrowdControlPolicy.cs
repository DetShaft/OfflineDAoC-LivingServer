using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Pure rules for the PvP crowd-control turn (#152, #151). The same rules apply to every
    /// realm's CC classes; only the spell data differs. Sources: docs/PVP-BEHAVIOR-RESEARCH.md
    /// questions 13-14 (60 s mez immunity since 1.42; area mez is an opener, not a rotation).
    /// </summary>
    public static class BotPvpCrowdControlPolicy
    {
        /// <summary>An area mez needs at least this many free, non-immune enemies around its centre.</summary>
        public const int MinAreaTargets = 2;

        /// <summary>After an area mez attempt the bot holds area mez this long (the mez immunity length).</summary>
        public const int AreaMezHoldMilliseconds = 60_000;

        /// <summary>
        /// An area mez with a cast this long is a backup line (the Minstrel's 5 s Lullaby, added in 1.51
        /// as a secondary mez), so it is tried after every other CC spell. Primary area mezzes
        /// (Sorcerer, Bard, Healer) cast in 3 s or less and keep their place as the opener.
        /// </summary>
        public const int SlowAreaMezCastMilliseconds = 4_000;

        public static bool IsAreaMez(Spell spell) => spell != null && spell.SpellType == eSpellType.Mesmerize && spell.Radius > 0;

        public static bool IsSlowAreaMez(Spell spell) => IsAreaMez(spell) && spell.CastTime >= SlowAreaMezCastMilliseconds;

        /// <summary>
        /// Order to try CC spells: slow (backup) area mezzes last; otherwise mez before other CC,
        /// larger radius first, then higher level.
        /// </summary>
        public static IEnumerable<Spell> Order(IEnumerable<Spell> spells) => (spells ?? Enumerable.Empty<Spell>())
            .Where(s => s != null)
            .OrderBy(IsSlowAreaMez)
            .ThenByDescending(s => s.SpellType == eSpellType.Mesmerize)
            .ThenByDescending(s => s.Radius)
            .ThenByDescending(s => s.Level);

        /// <summary>True when the area mez hold has run out. <paramref name="lastAttempt"/> is null when never tried.</summary>
        public static bool AreaMezReady(long now, long? lastAttempt) =>
            lastAttempt == null || now - lastAttempt.Value >= AreaMezHoldMilliseconds;

        /// <summary>True when enough free enemies stand in the area to be worth the cast.</summary>
        public static bool AreaClusterWorthCasting(int freeEnemiesInArea) => freeEnemiesInArea >= MinAreaTargets;

        /// <summary>
        /// Mez and root break on damage, so they are never aimed at an enemy groupmates are already hitting.
        /// Stuns and snares still land usefully on a target under attack.
        /// </summary>
        public static bool BreaksOnDamage(BotCasterSurvival.ControlKind kind) =>
            kind is BotCasterSurvival.ControlKind.Mez or BotCasterSurvival.ControlKind.Root;
    }
}
