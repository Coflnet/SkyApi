using System.Diagnostics;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Helper
{
    /// <summary>Contains activity logging tests.</summary>
    public class TraceExtensionsTests
    {
        /// <summary>Unrecorded activities skip building and storing the message.</summary>
        [Test]
        public void LazyLog_SkipsFactoryWhenActivityIsNotRecorded()
        {
            using var activity = new Activity("test") { IsAllDataRequested = false };
            var invoked = false;

            activity.Log(() => { invoked = true; return "expensive"; });
            activity.Log("plain");

            invoked.Should().BeFalse();
            activity.Events.Should().BeEmpty();
        }

        /// <summary>Recorded activities get the built message as an event.</summary>
        [Test]
        public void LazyLog_AddsEventWhenActivityIsRecorded()
        {
            using var activity = new Activity("test") { IsAllDataRequested = true };

            activity.Log(() => "expensive");

            activity.Events.Single().Tags.Single(t => t.Key == "message").Value.Should().Be("expensive");
        }

        /// <summary>Missing activity is a no-op.</summary>
        [Test]
        public void LazyLog_NullActivityDoesNotInvokeFactory()
        {
            Activity activity = null;
            var invoked = false;

            activity.Log(() => { invoked = true; return "expensive"; });

            invoked.Should().BeFalse();
        }
    }
}
