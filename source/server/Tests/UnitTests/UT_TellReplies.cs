using System;
using System.Linq;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>/reply (/r): answers whoever sent the last private message (owner 2026-11-11: replying didn't work).</summary>
    [TestFixture]
    public class UT_TellReplies
    {
        [SetUp]
        public void Reset() => TellReplies.Clear();

        [Test]
        public void ReplyIsRegisteredAsReplyAndR()
        {
            string[] names = typeof(ReplyCommandHandler).GetCustomAttributes(typeof(CmdAttribute), false)
                .Cast<CmdAttribute>()
                .SelectMany(a => new[] { a.Cmd }.Concat(a.Aliases ?? Array.Empty<string>()))
                .ToArray();
            Assert.That(names, Does.Contain("&reply"));
            Assert.That(names, Does.Contain("&r"));
        }

        [Test]
        public void TheLastSenderWinsAndNamesIgnoreCase()
        {
            Assert.That(TellReplies.TryGetLastSender("Jay", out _), Is.False);
            TellReplies.Record("Jay", "Aldwin");
            TellReplies.Record("jay", "Brennos");
            Assert.That(TellReplies.TryGetLastSender("JAY", out string sender), Is.True);
            Assert.That(sender, Is.EqualTo("Brennos"));
        }

        [Test]
        public void YourOwnTellsAndBlankNamesAreNotRecorded()
        {
            TellReplies.Record("Jay", "Jay");
            TellReplies.Record("Jay", " ");
            TellReplies.Record(null, "Aldwin");
            Assert.That(TellReplies.TryGetLastSender("Jay", out _), Is.False);
        }

        [Test]
        public void SendArgsAreTheSendCommandForTheSender()
        {
            Assert.That(TellReplies.SendArgs("Aldwin", new[] { "&r", "sure", "inv", "me" }),
                Is.EqualTo(new[] { "&send", "Aldwin", "sure", "inv", "me" }));
            Assert.That(TellReplies.SendArgs("Aldwin", new[] { "&reply" }), Is.EqualTo(new[] { "&send", "Aldwin" }));
        }
    }
}
