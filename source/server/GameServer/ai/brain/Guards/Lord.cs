using DOL.GS;
using DOL.GS.Keeps;
using System;

namespace DOL.AI.Brain
{
	/// <summary>
	/// The Brain for the Area Lord
	/// </summary>
	public class LordBrain : KeepGuardBrain
	{
		public LordBrain() : base() { }

		/// <summary>
		/// #162 (Killaloe, 1.1.23): Jarl Killaloe aggroed bots on the floor below his room (his room 9345, the floor below
		/// 9107). The navmesh line-of-sight ray is two-dimensional, so it passed under his floor, and he pulled the group
		/// into a fight on the bottom floor while the casters never came up. A lord now aggroes only targets on his own
		/// floor, less than a storey (about 240) above or below him. Damage done to him still brings him into a fight.
		/// </summary>
		public const int LordFloorHeight = 160;

		public override int MaxAggroHeightDifference => LordFloorHeight;

		public override void Think()
		{
			if (Body != null && Body.Spells.Count == 0)
			{
				switch (Body.Realm)
				{
					case eRealm.None:
					case eRealm.Albion:
						Body.Spells.Add(GuardSpellDB.AlbLordHealSpell);
						break;
					case eRealm.Midgard:
						Body.Spells.Add(GuardSpellDB.MidLordHealSpell);
						break;
					case eRealm.Hibernia:
						Body.Spells.Add(GuardSpellDB.HibLordHealSpell);
						break;
				}
			}
			base.Think();
		}
		
		private long m_nextCallForHelpTime = 0; // Used to limit how often keep lords call for help
		/// <summary>
		/// Bring all alive keep guards to defend the lord
		/// </summary>
		/// <param name="trigger">The entity which triggered the BAF.</param>
		protected override void BringFriends(GameLiving trigger)
		{
			if (GameServer.Instance.Configuration.ServerType == EGameServerType.GST_PvE)
			{
				GuardLord lord = Body as GuardLord;
				long currentTime = GameLoop.GameLoopTime;

				if (lord != null && m_nextCallForHelpTime < currentTime)
				{
					// Don't call for help more than once every minute
					m_nextCallForHelpTime = currentTime + 60000;

					int iGuardsResponding = 0;

					foreach (GameKeepGuard guard in lord.Component.Keep.Guards.Values)
						if (guard != null && guard.IsAlive && guard.IsAvailableToJoinFight)
							if (guard.AssistLord(lord))
								iGuardsResponding++;

					string sMessage = $"{lord.Name} bellows for assistance ";
					if (iGuardsResponding == 0)
						sMessage += "but no guards respond!";
					else
						sMessage += $"and {iGuardsResponding} guards respond!";

					foreach (GamePlayer player in lord.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
						if (player != null)
							ChatUtil.SendErrorMessage(player, sMessage);
				}
			}
			else
				base.BringFriends(trigger);
		}
	}// LordBrain
}
