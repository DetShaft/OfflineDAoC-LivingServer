using System;
using System.Runtime.CompilerServices;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// The pet window's ASSIST mode (Living Server): the player's pet fights as Defensive does, and
    /// also attacks the player's target the moment the player engages it (a melee or ranged attack,
    /// or the start of a harmful spell), the way party bots assist their leader
    /// (PlayerLedPullCoordinator / BotBrain.AssistPlayerAttack). It follows the player onto a new
    /// target, too.
    ///
    /// The choice belongs to the player, not to one pet: a pet summoned later assists as well, until
    /// the player picks Aggressive, Defensive or Passive. It applies to the main pet only (the one
    /// the pet window controls); a Bonedancer commander's minions follow the commander's target
    /// through its own assist order. Bot pets, which their bot's AI commands, are never affected.
    /// The client is still told "defensive": the classic pet window knows only three states.
    /// </summary>
    public static class PetAssistMode
    {
        /// <summary>How far from the player (and from the pet) the target may be. Matches the party bots' assist radius.</summary>
        public const int AssistRadius = 2000;

        private sealed class Preference
        {
            public bool On;
            public WeakReference<GameLiving> LastAssisted;
        }

        private static readonly ConditionalWeakTable<GamePlayer, Preference> Preferences = new();

        public static bool IsOn(GamePlayer player)
        {
            return player != null && Preferences.TryGetValue(player, out Preference preference) && preference.On;
        }

        /// <summary>
        /// Turns the player's assist mode on or off. On puts the current pet in Defensive, which is
        /// what Assist builds on (a passive pet would not fight, an aggressive one picks its own fights).
        /// </summary>
        public static void Set(GamePlayer player, bool on)
        {
            if (player == null)
                return;

            Preference preference = Preferences.GetOrCreateValue(player);
            preference.On = on;
            preference.LastAssisted = null;

            if (on && player.ControlledBrain is ControlledMobBrain brain && brain.AggressionState is not eAggressionState.Defensive)
                brain.SetAggressionState(eAggressionState.Defensive);
        }

        /// <summary>True when this brain is a player's main pet in Defensive and its owner has Assist on.</summary>
        public static bool Assists(ControlledMobBrain brain)
        {
            return brain?.Owner is GamePlayer owner &&
                ReferenceEquals(owner.ControlledBrain, brain) &&
                brain.AggressionState is eAggressionState.Defensive &&
                IsOn(owner);
        }

        /// <summary>
        /// The player has just attacked or started a harmful spell on target. Called from
        /// AttackComponent (start of an attack) and SpellHandler (start of a harmful cast).
        /// </summary>
        public static void OwnerEngaged(GamePlayer owner, GameLiving target)
        {
            if (owner?.ControlledBrain is ControlledMobBrain brain && Assists(brain))
                TryAssist(brain, owner, target);
        }

        /// <summary>
        /// Run on the pet's own think: picks up an engagement the event missed (the player switched
        /// targets while auto-attack stayed on, or the pet was summoned mid-fight). A manual ATTACK
        /// order stands until the player moves on to a different target.
        /// </summary>
        public static bool CheckOwnerTarget(ControlledMobBrain brain)
        {
            if (!Assists(brain))
                return false;

            GamePlayer owner = (GamePlayer) brain.Owner;
            GameLiving target = OwnerTarget(owner);
            if (target == null || ReferenceEquals(brain.OrderedAttackTarget, target))
                return false;

            Preference preference = Preferences.GetOrCreateValue(owner);
            GameLiving last = null;
            preference.LastAssisted?.TryGetTarget(out last);
            bool petBusy = brain.OrderedAttackTarget?.IsAlive == true;
            if (petBusy && ReferenceEquals(last, target))
                return false;

            return TryAssist(brain, owner, target);
        }

        /// <summary>The living the player is fighting right now: their target while they attack or cast a harmful spell.</summary>
        public static GameLiving OwnerTarget(GamePlayer owner)
        {
            if (owner?.IsAlive != true)
                return null;

            bool engaging = owner.IsAttacking ||
                owner.IsCasting && owner.castingComponent?.SpellHandler?.Spell?.IsHarmful == true;
            return engaging ? owner.TargetObject as GameLiving : null;
        }

        /// <summary>Whether the pet may be sent at target for its owner: alive, in reach of both, and attackable.</summary>
        public static bool IsValidTarget(GameNPC pet, GamePlayer owner, GameLiving target)
        {
            return pet?.IsAlive == true && owner != null && target != null &&
                !ReferenceEquals(target, pet) && !ReferenceEquals(target, owner) &&
                target.IsAlive && target.ObjectState == GameObject.eObjectState.Active &&
                target.CurrentRegionID == owner.CurrentRegionID && pet.CurrentRegionID == owner.CurrentRegionID &&
                owner.IsWithinRadius(target, AssistRadius) && pet.IsWithinRadius(target, AssistRadius) &&
                GameServer.ServerRules.IsAllowedToAttack(pet, target, true);
        }

        private static bool TryAssist(ControlledMobBrain brain, GamePlayer owner, GameLiving target)
        {
            if (!IsValidTarget(brain.Body, owner, target))
                return false;

            Preferences.GetOrCreateValue(owner).LastAssisted = new WeakReference<GameLiving>(target);
            if (ReferenceEquals(brain.OrderedAttackTarget, target))
                return false;

            brain.Attack(target);
            return ReferenceEquals(brain.OrderedAttackTarget, target);
        }
    }
}
