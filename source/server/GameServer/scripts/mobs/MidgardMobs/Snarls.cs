using DOL.AI.Brain;
using DOL.GS;

namespace DOL.GS
{
	public class Snarls : GameNPC
	{
		public Snarls() : base() { }

		public override bool AddToWorld()
		{
			INpcTemplate npcTemplate = NpcTemplateMgr.GetTemplate(60157490);
			LoadTemplate(npcTemplate);
			//RespawnInterval = Util.Random(3600000, 7200000);

			SnarlsAdd.SnarlsAddCount = 0;
			SnarlsBrain sbrain = new SnarlsBrain();
			SetOwnBrain(sbrain);
			LoadedFromScript = false;//load from database
			SaveIntoDatabase();
			base.AddToWorld();
			return true;
		}
	}
}
namespace DOL.AI.Brain
{
	public class SnarlsBrain : StandardMobBrain
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
		public SnarlsBrain() : base()
		{
			AggroLevel = 80;
			AggroRange = 400;
			ThinkInterval = 1000;
		}
		/// <summary>Snarls stays hidden until this many of its wolves have died.</summary>
		public const int AddsToReveal = 3;

		/// <summary>What hides Snarls: no target, no name, and no fighting (PEACE) until its wolves are dead.</summary>
		public const GameNPC.eFlags HiddenFlags = GameNPC.eFlags.CANTTARGET | GameNPC.eFlags.DONTSHOWNAME | GameNPC.eFlags.PEACE;

		ushort oldModel;
		GameNPC.eFlags oldFlags;
		bool changed;

		/// <summary>True while Snarls is hidden (its wolves are not dead yet).</summary>
		public bool IsHidden => changed;

		public override void Think()
		{
			if (SnarlsAdd.SnarlsAddCount >= AddsToReveal)
				Reveal();
			else
				Hide();

			if (changed)
			{
				// A hidden Snarls never fights: it can't be targeted back. Drop anything it picked up (a wolf's call for
				// help, a bot's area spell) so it does not attack the moment it is revealed or while it is still hidden.
				if (HasAggro)
					ClearAggroList();
				if (Body.IsAttacking)
					Body.StopAttack();
				if (Body.TargetObject != null)
					Body.TargetObject = null;
				base.Think();
				return;
			}

			if(HasAggro && Body.TargetObject != null)
            {
				foreach (GameNPC npc in Body.GetNPCsInRadius(1000))
				{
					if (npc != null && npc.IsAlive && npc.Brain is SnarlsAddBrain brain)
					{
						GameLiving target = Body.TargetObject as GameLiving;
						if (target != null && target.IsAlive && brain != null && !brain.HasAggro)
							brain.AddToAggroList(target, 10);
					}
				}
			}
			base.Think();
		}

		/// <summary>
		/// Hides Snarls (the model first, so the client's new copy is already the hidden one). The hidden flags are set,
		/// not toggled: toggling would clear one the template already has
		/// (a template with PEACE would leave a hidden Snarls able to fight while it can't be targeted).
		/// </summary>
		public void Hide()
		{
			if (changed)
				return;

			oldFlags = Body.Flags;
			if (oldModel == 0)
				oldModel = Body.Model;

			changed = true;
			Body.Model = 1;
			Body.Flags = oldFlags | HiddenFlags;
		}

		/// <summary>
		/// Shows Snarls again, targetable and named. The model goes back first, so the client's new copy (sent when the
		/// flags change) already is the wolf.
		/// </summary>
		public void Reveal()
		{
			if (!changed)
				return;

			changed = false;
			if (oldModel != 0)
				Body.Model = oldModel;
			Body.Flags = oldFlags & ~HiddenFlags;
		}
	}
}

namespace DOL.GS
{
	public class SnarlsAdd : GameNPC
	{
		public SnarlsAdd() : base() { }

		public override bool AddToWorld()
		{
			INpcTemplate npcTemplate = NpcTemplateMgr.GetTemplate(60163490);
			LoadTemplate(npcTemplate);

			SnarlsAddBrain sbrain = new SnarlsAddBrain();
			SetOwnBrain(sbrain);
			LoadedFromScript = false;//load from database
			SaveIntoDatabase();
			base.AddToWorld();
			return true;
		}
		public static int SnarlsAddCount = 0;
        public override void Die(GameObject killer)
        {
			++SnarlsAddCount;
            base.Die(killer);
        }
    }
}
namespace DOL.AI.Brain
{
	public class SnarlsAddBrain : StandardMobBrain
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
		public SnarlsAddBrain() : base()
		{
			AggroLevel = 0;
			AggroRange = 400;
			ThinkInterval = 1500;
		}
		public override void Think()
		{
			base.Think();
		}
	}
}