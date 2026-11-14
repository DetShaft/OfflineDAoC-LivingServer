/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System;
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS.DatabaseUpdate
{
    /// <summary>
    /// The 15-19 battleground stays region 253. Its name becomes Killaloe. Offsets, keeps and
    /// portals are not moved. A second start finds nothing left to rename.
    /// Region 253 is unchanged, so a startup copy of portal-keep guards into this region still
    /// applies here. This rename does not depend on that copy.
    /// </summary>
    [DatabaseUpdate]
    public class KillaloeBattlegroundUpdate : IDatabaseUpdater
    {
        public const ushort RegionId = 253;

        private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static string Rename(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return value.Replace("Abermenai", "Killaloe").Replace("abermenai", "killaloe");
        }

        public void Update()
        {
            int changed = 0;
            changed += RenameRegion();
            changed += RenameZones();
            var areaIds = RenameAreas();
            changed += areaIds.Changed;
            changed += RenameLanguageZones();
            changed += RenameLanguageAreas(areaIds.TranslationIds);
            changed += RenameKeeps();
            changed += RenameTeleports();
            if (changed == 0)
                log.Info("Killaloe: region 253 already uses that name.");
            else
                log.Info($"Killaloe: renamed {changed} rows. Region 253 is the 15-19 battleground.");
        }

        private static int RenameRegion()
        {
            int changed = 0;
            foreach (DbRegion region in GameServer.Database.SelectAllObjects<DbRegion>())
            {
                if (region.RegionID != RegionId)
                    continue;
                if (Apply(region.Name, value => region.Name = value) | Apply(region.Description, value => region.Description = value))
                {
                    GameServer.Database.SaveObject(region);
                    changed++;
                }
            }
            return changed;
        }

        private static int RenameZones()
        {
            int changed = 0;
            foreach (DbZone zone in GameServer.Database.SelectAllObjects<DbZone>())
            {
                if (zone.RegionID != RegionId && zone.ZoneID != RegionId)
                    continue;
                if (Apply(zone.Name, value => zone.Name = value))
                {
                    GameServer.Database.SaveObject(zone);
                    changed++;
                }
            }
            return changed;
        }

        private static (int Changed, HashSet<string> TranslationIds) RenameAreas()
        {
            int changed = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (DbArea area in GameServer.Database.SelectAllObjects<DbArea>())
            {
                if (area.Region != RegionId)
                    continue;
                if (!string.IsNullOrEmpty(area.TranslationId))
                    ids.Add(area.TranslationId);
                if (Apply(area.Description, value => area.Description = value))
                {
                    GameServer.Database.SaveObject(area);
                    changed++;
                }
            }
            return (changed, ids);
        }

        private static int RenameLanguageZones()
        {
            int changed = 0;
            foreach (DbLanguageZone row in GameServer.Database.SelectAllObjects<DbLanguageZone>())
            {
                if (row.TranslationId != RegionId.ToString())
                    continue;
                if (Apply(row.Description, value => row.Description = value) | Apply(row.ScreenDescription, value => row.ScreenDescription = value))
                {
                    GameServer.Database.SaveObject(row);
                    changed++;
                }
            }
            return changed;
        }

        private static int RenameLanguageAreas(HashSet<string> translationIds)
        {
            if (translationIds.Count == 0)
                return 0;
            int changed = 0;
            foreach (DbLanguageArea row in GameServer.Database.SelectAllObjects<DbLanguageArea>())
            {
                if (string.IsNullOrEmpty(row.TranslationId) || !translationIds.Contains(row.TranslationId))
                    continue;
                if (Apply(row.Description, value => row.Description = value) | Apply(row.ScreenDescription, value => row.ScreenDescription = value))
                {
                    GameServer.Database.SaveObject(row);
                    changed++;
                }
            }
            return changed;
        }

        private static int RenameKeeps()
        {
            int changed = 0;
            foreach (DbKeep keep in GameServer.Database.SelectAllObjects<DbKeep>())
            {
                if (keep.Region != RegionId)
                    continue;
                if (Apply(keep.Name, value => keep.Name = value))
                {
                    GameServer.Database.SaveObject(keep);
                    changed++;
                }
            }
            return changed;
        }

        private static int RenameTeleports()
        {
            int changed = 0;
            foreach (DbTeleport teleport in GameServer.Database.SelectAllObjects<DbTeleport>())
            {
                if (teleport.RegionID != RegionId)
                    continue;
                if (Apply(teleport.TeleportID, value => teleport.TeleportID = value))
                {
                    GameServer.Database.SaveObject(teleport);
                    changed++;
                }
            }
            return changed;
        }

        private static bool Apply(string current, Action<string> set)
        {
            string next = Rename(current);
            if (next == current)
                return false;
            set(next);
            return true;
        }
    }
}
