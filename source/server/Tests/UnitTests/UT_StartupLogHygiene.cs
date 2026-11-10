using System;
using DOL.GS;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_StartupLogHygiene
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(UT_StartupLogHygiene));

        [Test]
        public void WarnOnceReportsOnlyFirstKey()
        {
            string category = "TestCategory-" + Guid.NewGuid().ToString("N");
            const string key = "example:1";

            Assert.That(StartupLogHygiene.WarnOnce(Log, category, key, "first"), Is.True);
            Assert.That(StartupLogHygiene.WarnOnce(Log, category, key, "second"), Is.False);
            Assert.That(StartupLogHygiene.WarnOnce(Log, category, "example:2", "other"), Is.True);
        }
    }
}
