using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;

namespace CCSWE.Avalonia.Hosting.Desktop.UnitTests;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class HostStopBridgeTests
{
    private readonly List<Action> _posted = [];
    private readonly Action _shutdown = () => { };

    private HostStopBridge CreateBridge() => new(_shutdown);

    private void Post(Action action) => _posted.Add(action);

    public class When_Attach_Is_Called : HostStopBridgeTests
    {
        [Test]
        public void It_posts_the_shutdown_when_the_host_already_stopped()
        {
            var bridge = CreateBridge();
            bridge.RequestStop();

            bridge.Attach(Post);

            Assert.That(_posted, Is.EqualTo(new[] { _shutdown }));
        }

        [Test]
        public void It_posts_nothing_while_the_host_runs()
        {
            var bridge = CreateBridge();

            bridge.Attach(Post);

            Assert.That(_posted, Is.Empty);
        }
    }

    public class When_RequestStop_Is_Called : HostStopBridgeTests
    {
        [Test]
        public void It_posts_the_shutdown_when_attached()
        {
            var bridge = CreateBridge();
            bridge.Attach(Post);

            bridge.RequestStop();

            Assert.That(_posted, Is.EqualTo(new[] { _shutdown }));
        }

        [Test]
        public void It_posts_nothing_after_detach()
        {
            var bridge = CreateBridge();
            bridge.Attach(Post);
            bridge.Detach();

            bridge.RequestStop();

            Assert.That(_posted, Is.Empty);
        }

        [Test]
        public void It_posts_nothing_before_attach()
        {
            var bridge = CreateBridge();

            bridge.RequestStop();

            Assert.That(_posted, Is.Empty);
        }
    }
}
