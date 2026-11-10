using System.Collections.Generic;

namespace DOL.GS.PacketHandler.Client.v168
{
	[PacketHandlerAttribute(PacketHandlerType.TCP, eClientPackets.LookingForGroup, "handle Looking for a group", eClientStatus.PlayerInGame)]
	public class LookingForAGroupHandler : PacketHandler
	{
		protected override void HandlePacketInternal(GameClient client, GSPacketIn packet)
		{
			byte grouped = (byte)packet.ReadByte();
			// Mixed list: open groups (when requested), flagged players, and advertising / seeking gamebots.
			IList<LfgWindowEntry> entries = LfgBrowser.BuildFor(client.Player);
			if (grouped == 0x00)
			{
				// Solo-only view: drop open-group leaders.
				var solos = new List<LfgWindowEntry>();
				foreach (LfgWindowEntry entry in entries)
				{
					if (!entry.IsGrouped)
						solos.Add(entry);
				}
				entries = solos;
			}

			var array = new LfgWindowEntry[entries.Count];
			for (int i = 0; i < entries.Count; i++)
				array[i] = entries[i];
			client.Out.SendFindGroupWindowUpdate(array);
		}
	}
}
