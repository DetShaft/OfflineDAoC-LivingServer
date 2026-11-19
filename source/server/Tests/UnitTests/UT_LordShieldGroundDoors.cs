using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #157: the keep lord is shielded from bots only while a door on the way in stands. Dun Killaloe, 2026-11-19: the
    /// gate (Z 8768) was open and the keep door (Z 8770) broken, but two gatehouse doors at Z 9200 kept the lord shielded.
    /// </summary>
    [TestFixture]
    public class UT_LordShieldGroundDoors
    {
        [Test]
        public void GateAndKeepDoorCount()
        {
            Assert.That(GuardLord.BlocksTheWayIn(8768, 8768), Is.True, "The gate itself.");
            Assert.That(GuardLord.BlocksTheWayIn(8770, 8768), Is.True, "The keep door, at ground level.");
            Assert.That(GuardLord.BlocksTheWayIn(8768 + GuardLord.GroundDoorHeight, 8768), Is.True);
        }

        [Test]
        public void UpperGatehouseDoorsDoNotShieldTheLord()
        {
            Assert.That(GuardLord.BlocksTheWayIn(9200, 8768), Is.False, "Dun Killaloe doors 253138003 and 253138004.");
            Assert.That(GuardLord.BlocksTheWayIn(8768 + GuardLord.GroundDoorHeight + 1, 8768), Is.False);
        }
    }
}
