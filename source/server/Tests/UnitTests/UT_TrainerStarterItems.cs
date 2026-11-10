using DOL.Database;
using DOL.GS;
using DOL.GS.Trainer;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_TrainerStarterItems
    {
        [Test]
        public void BonedancerStarterUsesStaffTemplateFields()
        {
            var source = new DbItemTemplate
            {
                Id_nb = SpiritmasterTrainer.WEAPON_ID,
                Name = "Spiritmaster Staff",
                Model = 300,
                Realm = (int)eRealm.Midgard,
                Object_Type = (int)eObjectType.Staff,
                Item_Type = (int)eInventorySlot.TwoHandWeapon,
            };

            DbItemTemplate built = TrainerStarterItems.BuildBonedancerStarter(source);

            Assert.Multiple(() =>
            {
                Assert.That(built.Id_nb, Is.EqualTo(BonedancerTrainer.WEAPON_ID1));
                Assert.That(built.Model, Is.EqualTo(3464));
                Assert.That(built.Realm, Is.EqualTo((int)eRealm.Midgard));
                Assert.That(built.Object_Type, Is.EqualTo((int)eObjectType.Staff));
                Assert.That(built.IsDropable, Is.False);
                Assert.That(built.IsPickable, Is.True);
            });
        }

        [Test]
        public void BonedancerStarterWithoutSourceStillBuildsStaff()
        {
            DbItemTemplate built = TrainerStarterItems.BuildBonedancerStarter(null);
            Assert.That(built.Id_nb, Is.EqualTo(BonedancerTrainer.WEAPON_ID1));
            Assert.That(built.Model, Is.EqualTo(3464));
        }
    }
}
