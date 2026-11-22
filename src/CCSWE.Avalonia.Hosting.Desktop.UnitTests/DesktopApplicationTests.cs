using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;

namespace CCSWE.Avalonia.Hosting.Desktop.UnitTests;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class DesktopApplicationTests
{
    // A host whose StopAsync throws, as Microsoft.Extensions.Hosting does when a hosted service fails to stop.
    private static Mock<IHost> CreateHost(Exception? stopException = null)
    {
        var host = new Mock<IHost>();
        host.Setup(h => h.Services).Returns(new ServiceCollection().BuildServiceProvider());

        if (stopException is not null)
        {
            host.Setup(h => h.StopAsync(It.IsAny<CancellationToken>())).ThrowsAsync(stopException);
        }

        return host;
    }

    public class When_CreateBuilder_Is_Called : DesktopApplicationTests
    {
        [Test]
        public void It_exposes_the_service_collection()
        {
            var builder = DesktopApplication.CreateBuilder<TestApp>([]);

            Assert.That(builder.Services, Is.Not.Null);
        }

        [Test]
        public void It_returns_the_same_builder_from_ConfigureAppBuilder()
        {
            var builder = DesktopApplication.CreateBuilder<TestApp>([]);

            var result = builder.ConfigureAppBuilder(appBuilder => appBuilder);

            Assert.That(result, Is.SameAs(builder));
        }
    }

    public class When_Build_Is_Called : DesktopApplicationTests
    {
        [Test]
        public void It_builds_an_application_whose_provider_resolves_registered_services()
        {
            var builder = DesktopApplication.CreateBuilder<TestApp>([]);
            builder.Services.AddSingleton<IExampleService, ExampleService>();

            var application = builder.Build();

            Assert.That(application.Services.GetService<IExampleService>(), Is.InstanceOf<ExampleService>());
        }
    }

    public class When_Run_Is_Called : DesktopApplicationTests
    {
        [Test]
        public void It_propagates_the_startup_exception_when_stopping_also_fails()
        {
            var host = CreateHost(new InvalidOperationException("stop"));
            var startupException = new InvalidOperationException("startup");
            var application = new DesktopApplication(host.Object, () => throw startupException, ShutdownMode.OnLastWindowClose);

            var exception = Assert.Throws<InvalidOperationException>(() => application.Run([]));

            Assert.That(exception, Is.SameAs(startupException));
            host.Verify(h => h.Dispose(), Times.Once());
        }
    }

    public class When_StopHost_Is_Called : DesktopApplicationTests
    {
        [Test]
        public void It_disposes_the_host_and_propagates_a_stop_failure_when_rethrowing()
        {
            var stopException = new InvalidOperationException("stop");
            var host = CreateHost(stopException);
            var application = new DesktopApplication(host.Object, AppBuilder.Configure<TestApp>, ShutdownMode.OnLastWindowClose);

            var exception = Assert.Throws<InvalidOperationException>(() => application.StopHost(rethrow: true));

            Assert.That(exception, Is.SameAs(stopException));
            host.Verify(h => h.Dispose(), Times.Once());
        }

        [Test]
        public void It_disposes_the_host_and_swallows_a_stop_failure_when_not_rethrowing()
        {
            var host = CreateHost(new InvalidOperationException("stop"));
            var application = new DesktopApplication(host.Object, AppBuilder.Configure<TestApp>, ShutdownMode.OnLastWindowClose);

            Assert.DoesNotThrow(() => application.StopHost(rethrow: false));

            host.Verify(h => h.Dispose(), Times.Once());
        }

        [Test]
        public void It_disposes_the_host_after_a_clean_stop()
        {
            var host = CreateHost();
            var application = new DesktopApplication(host.Object, AppBuilder.Configure<TestApp>, ShutdownMode.OnLastWindowClose);

            application.StopHost(rethrow: true);

            host.Verify(h => h.StopAsync(It.IsAny<CancellationToken>()), Times.Once());
            host.Verify(h => h.Dispose(), Times.Once());
        }
    }

    private interface IExampleService;

    private sealed class ExampleService : IExampleService;

    private sealed class TestApp : Application;
}
