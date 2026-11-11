using DOL.GS.ServerProperties;
using DOL.GS.Styles;

namespace DOL.GS
{
    public class NpcStyleComponent : StyleComponent
    {
        private GameNPC _npcOwner;

        public NpcStyleComponent(GameNPC npcOwner) : base(npcOwner)
        {
            _npcOwner = npcOwner;
        }

        public override Style GetStyleToUse()
        {
            // We return `NextCombatStyle` if it was overwritten.
            // It should be set back to null once used.
            if (NextCombatStyle != null)
            {
                // A preferred style from the previous weapon stays in NextCombatStyle until
                // something clears it. Do not fire it on the new weapon.
                if (_npcOwner is CommanderPet commander && !commander.StyleMatchesWeapon(NextCombatStyle))
                    commander.SetPreferredCombatStyle(null);
                else
                    return NextCombatStyle;
            }

            if (_npcOwner.Styles == null || _npcOwner.Styles.Count < 1 || _npcOwner.TargetObject == null)
                return null;

            AttackData lastAttackData = _npcOwner.attackComponent.attackAction.LastAttackData;

            // Chain and defensive styles are excluded from the chance roll because they would almost never happen otherwise. 
            // For example, an NPC blocks 10% of the time, so the default 20% style chance effectively means the defensive 
            // style would only actually occur during 2% of of a mob's attacks. In comparison, a style chain would only happen 
            // 0.4% of the time.
            if (_npcOwner.StylesChain != null && _npcOwner.StylesChain.Count > 0)
            {
                foreach (Style style in _npcOwner.StylesChain)
                {
                    if (StyleProcessor.CanUseStyle(lastAttackData, _npcOwner, style, _npcOwner.ActiveWeapon))
                        return style;
                }
            }

            if (_npcOwner.StylesDefensive != null && _npcOwner.StylesDefensive.Count > 0)
            {
                foreach (Style style in _npcOwner.StylesDefensive)
                {
                    if (StyleProcessor.CanUseStyle(lastAttackData, _npcOwner, style, _npcOwner.ActiveWeapon) && _npcOwner.CheckStyleStun(style)) // Make sure we don't spam stun styles like Brutalize
                        return style;
                }
            }

            if (Util.Chance(Properties.GAMENPC_CHANCES_TO_STYLE))
            {
                // All of the remaining lists are randomly picked from,
                // as this creates more variety with each combat result.
                // For example, a mob with both Pincer and Ice Storm
                // styles could potentially use one or the other with
                // each attack roll that succeeds.

                // First, check positional styles (in order of back, side, front)
                // in case the defender is facing another direction
                if (_npcOwner.StylesBack != null && _npcOwner.StylesBack.Count > 0)
                {
                    Style style = PickRandom(_npcOwner.StylesBack);

                    if (style != null && StyleProcessor.CanUseStyle(lastAttackData, _npcOwner, style, _npcOwner.ActiveWeapon))
                        return style;
                }

                if (_npcOwner.StylesSide != null && _npcOwner.StylesSide.Count > 0)
                {
                    Style style = PickRandom(_npcOwner.StylesSide);

                    if (style != null && StyleProcessor.CanUseStyle(lastAttackData, _npcOwner, style, _npcOwner.ActiveWeapon))
                        return style;
                }

                if (_npcOwner.StylesFront != null && _npcOwner.StylesFront.Count > 0)
                {
                    Style style = PickRandom(_npcOwner.StylesFront);

                    if (style != null && StyleProcessor.CanUseStyle(lastAttackData, _npcOwner, style, _npcOwner.ActiveWeapon))
                        return style;
                }

                // Pick a random anytime style. A commander only picks one its weapon can use.
                if (_npcOwner.StylesAnytime != null && _npcOwner.StylesAnytime.Count > 0)
                    return PickRandom(_npcOwner.StylesAnytime);
            }

            return null;
        }

        /// <summary>
        /// A random style from the list. A commander knows the styles of every weapon it can hold
        /// (CommanderPet.RefreshLevelStyles), so it picks only among the equipped weapon's: a random
        /// pick from all of them would mostly land on another weapon's style and not fire.
        /// </summary>
        private Style PickRandom(System.Collections.Generic.List<Style> styles)
        {
            if (_npcOwner is not CommanderPet commander)
                return styles[Util.Random(0, styles.Count - 1)];

            var fitting = new System.Collections.Generic.List<Style>();
            foreach (Style style in styles)
            {
                if (commander.StyleMatchesWeapon(style))
                    fitting.Add(style);
            }
            return fitting.Count == 0 ? null : fitting[Util.Random(0, fitting.Count - 1)];
        }
    }
}
