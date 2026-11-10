using System;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS.Trainer;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Ensures class-promotion starter items referenced by trainers exist in the world database.
    /// </summary>
    public static class TrainerStarterItems
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly string[] BonedancerCloneSources =
        {
            SpiritmasterTrainer.WEAPON_ID,
            RunemasterTrainer.WEAPON_ID,
            "warlock_item",
            "shaman_item",
        };

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args) => EnsureBonedancerStarter();

        public static void EnsureBonedancerStarter()
        {
            if (GameServer.Database.FindObjectByKey<DbItemTemplate>(BonedancerTrainer.WEAPON_ID1) != null)
                return;

            DbItemTemplate created = CreateBonedancerStarterFromDatabase();
            if (created == null)
            {
                Log.Error($"Trainer starter item {BonedancerTrainer.WEAPON_ID1} is missing and could not be created from a Midgard caster template.");
                return;
            }

            if (GameServer.Database.AddObject(created))
                Log.Info($"Trainer starter item added: {created.Id_nb} (model {created.Model}, cloned from missing-row repair).");
            else
                Log.Warn($"Trainer starter item {BonedancerTrainer.WEAPON_ID1} could not be saved to the database.");
        }

        public static DbItemTemplate CreateBonedancerStarterFromDatabase()
        {
            foreach (string sourceId in BonedancerCloneSources)
            {
                if (GameServer.Database.FindObjectByKey<DbItemTemplate>(sourceId) is not DbItemTemplate source)
                    continue;

                return BuildBonedancerStarter(source);
            }

            return BuildBonedancerStarter(null);
        }

        public static DbItemTemplate BuildBonedancerStarter(DbItemTemplate source)
        {
            DbItemTemplate item = source == null ? new DbItemTemplate() : new DbItemTemplate(source);

            item.Id_nb = BonedancerTrainer.WEAPON_ID1;
            item.Name = "Bonedancer staff";
            item.Level = 1;
            item.Quality = Math.Max(item.Quality, 100);
            item.Model = 3464;
            item.Object_Type = (int)eObjectType.Staff;
            item.Type_Damage = (int)eDamageType.Crush;
            item.Item_Type = (int)eInventorySlot.TwoHandWeapon;
            item.Hand = (int)eActiveWeaponSlot.TwoHanded;
            item.Realm = (int)eRealm.Midgard;
            item.IsPickable = true;
            item.IsDropable = false;
            item.Price = 0;
            item.Weight = Math.Max(item.Weight, 1);
            item.Condition = Math.Max(item.Condition, 50000);
            item.MaxCondition = Math.Max(item.MaxCondition, 50000);
            item.Durability = Math.Max(item.Durability, 50000);
            item.MaxDurability = Math.Max(item.MaxDurability, 50000);
            item.MaxCount = 1;
            item.PackageID = "TrainerStarter";

            return item;
        }
    }
}
