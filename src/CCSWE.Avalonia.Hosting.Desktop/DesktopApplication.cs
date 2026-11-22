using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCSWE.Avalonia.Hosting;

/// <summary>
/// A runnable desktop application that owns a Generic <see cref="IHost"/> and an Avalonia classic-desktop
/// lifetime. <see cref="Run"/> starts the hosted services, runs the Avalonia main loop until the application
/// exits or the host stops, then stops the host. Create one with
/// <see cref="CreateBuilder{TApp}(string[], Action{DesktopApplicationOptions})"/> and
/// <see cref="DesktopApplicationBuilder.Build"/>.
/// </summary>
[PublicAPI]
public sealed partial class DesktopApplication
{
    private readonly Func<AppBuilder> _appBuilderFactory;
    private readonly IHost _host;
    private readonly ShutdownMode _shutdownMode;
    private int _started;

    internal DesktopApplication(IHost host, Func<AppBuilder> appBuilderFactory, ShutdownMode shutdownMode)
    {
        _host = host;
        _appBuilderFactory = appBuilderFactory;
        _shutdownMode = shutdownMode;
    }

    /// <summary>The underlying Generic Host, for advanced lifetime coordination
    /// (<see cref="IHostApplicationLifetime"/>, manual start/stop).</summary>
    public IHost Host => _host;

    /// <summary>The application's service provider (the host's root provider).</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>
    /// Produces the Avalonia <see cref="AppBuilder"/> for <typeparamref name="TApp"/> with the same defaults
    /// <see cref="CreateBuilder{TApp}(string[], Action{DesktopApplicationOptions})"/> applies, but with no Generic
    /// Host. Intended for the design-time XAML previewer, whose <c>BuildAvaloniaApp()</c> entry point needs a
    /// parameterless <see cref="AppBuilder"/>-returning method. The provider is never set at design time, so the
    /// application must tolerate a null provider.
    /// </summary>
    public static AppBuilder ConfigureAppBuilder<TApp>(Action<DesktopApplicationOptions>? configureOptions = null)
        where TApp : Application, new()
    {
        var options = new DesktopApplicationOptions();
        configureOptions?.Invoke(options);
        return options.Apply(AppBuilder.Configure<TApp>());
    }

    /// <summary>
    /// Creates a <see cref="DesktopApplicationBuilder"/> for the Avalonia application <typeparamref name="TApp"/>.
    /// Initializes the Generic Host and prepares an Avalonia <see cref="AppBuilder"/> with the defaults from
    /// <see cref="DesktopApplicationOptions"/> (which <paramref name="configureOptions"/> may override).
    /// </summary>
    /// <typeparam name="TApp">The Avalonia <c>Application</c> subclass (needs a parameterless constructor).
    /// Implement <see cref="IServiceProviderAccessor"/> on it to receive the built provider.</typeparam>
    public static DesktopApplicationBuilder CreateBuilder<TApp>(string[] args, Action<DesktopApplicationOptions>? configureOptions = null)
        where TApp : Application, new()
    {
        var options = new DesktopApplicationOptions();
        configureOptions?.Invoke(options);

        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        if (options.ClearLoggingProviders)
        {
            hostBuilder.Logging.ClearProviders();
        }

        if (options.SuppressStatusMessages)
        {
            hostBuilder.Services.Configure<ConsoleLifetimeOptions>(consoleOptions => consoleOptions.SuppressStatusMessages = true);
        }

        return new DesktopApplicationBuilder(hostBuilder, () => options.Apply(AppBuilder.Configure<TApp>()), options.ShutdownMode);
    }

    // StopHost raises ApplicationStopping again; the UI has exited, so stop listening first.
    private static void DetachHostStop(CancellationTokenRegistration registration, HostStopBridge bridge)
    {
        registration.Dispose();
        bridge.Detach();
    }

    // Hands the built provider to the application instance. A soft contract: an app that doesn't implement
    // IServiceProviderAccessor simply doesn't receive it (it likely has nothing to compose from DI).
    private void InjectServices(Application? application)
    {
        if (application is IServiceProviderAccessor accessor)
        {
            accessor.Services = _host.Services;
            return;
        }

        if (_host.Services.GetService<ILogger<DesktopApplication>>() is { } logger)
        {
            Log.ServiceProviderNotInjected(logger, application?.GetType().Name ?? nameof(Application), nameof(IServiceProviderAccessor));
        }
    }

    /// <summary>
    /// Runs the application: starts the hosted services (non-blocking), runs the Avalonia classic-desktop main
    /// loop (blocking) until the last window closes, shutdown is requested or the host stops, then stops the host
    /// gracefully (cancelling background services). This is the one call a typical <c>Program.Main</c> needs.
    /// </summary>
    /// <returns>The Avalonia process exit code.</returns>
    /// <exception cref="InvalidOperationException">The application has already been run.</exception>
    /// <remarks>
    /// <para>When the host stops while the UI runs (Ctrl+C or SIGTERM through the console lifetime,
    /// <c>IHostApplicationLifetime.StopApplication()</c>, or a faulted <c>BackgroundService</c> under the default
    /// <c>BackgroundServiceExceptionBehavior.StopHost</c>), the UI is shut down with a forced shutdown:
    /// <c>ShutdownRequested</c> isn't raised and window <c>Closing</c> handlers can't cancel it.
    /// <c>StopApplication()</c> is therefore also the way to quit an app that uses
    /// <c>ShutdownMode.OnExplicitShutdown</c>. If the host stops during startup, the UI isn't started.</para>
    /// <para>The host is always stopped and disposed. If startup or the UI loop throws, that exception propagates
    /// and a failure to stop the host is only logged. After a normal exit, a failure to stop the host (a hosted
    /// service failing to stop or, from Microsoft.Extensions.Hosting 11, a faulted <c>BackgroundService</c>)
    /// propagates.</para>
    /// </remarks>
    public int Run(string[] args)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException($"{nameof(DesktopApplication)} has already been run.");
        }

        IClassicDesktopStyleApplicationLifetime? desktopLifetime = null;
        var bridge = new HostStopBridge(() => desktopLifetime?.Shutdown());
        var registration = default(CancellationTokenRegistration);
        int exitCode;
        try
        {
            // Registered before Start, so a stop during startup (or a fault right after it) is never missed.
            var stopping = _host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
            registration = stopping.Register(bridge.RequestStop);

            try
            {
                // Non-blocking: runs each IHostedService.StartAsync and kicks off BackgroundService.ExecuteAsync.
                _host.Start();
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                // The host stopped during startup (StartAsync links ApplicationStopping into its token).
            }

            if (stopping.IsCancellationRequested)
            {
                exitCode = 0;
            }
            else
            {
                // AfterSetup runs on this thread after TApp is constructed + Initialize(), before
                // OnFrameworkInitializationCompleted, so the provider is set in time. Dispatcher.UIThread is only
                // touched here: touching it first from another thread would make that thread the UI thread.
                // StartWithClassicDesktopLifetime then blocks for the UI session.
                exitCode = _appBuilderFactory()
                    .AfterSetup(builder =>
                    {
                        InjectServices(builder.Instance);
                        var dispatcher = Dispatcher.UIThread;
                        bridge.Attach(action => dispatcher.Post(action, DispatcherPriority.Send));
                    })
                    .StartWithClassicDesktopLifetime(args, lifetime =>
                    {
                        lifetime.ShutdownMode = _shutdownMode;
                        desktopLifetime = lifetime;
                    });
            }
        }
        catch
        {
            DetachHostStop(registration, bridge);
            StopHost(rethrow: false);
            throw;
        }

        DetachHostStop(registration, bridge);
        StopHost(rethrow: true);
        return exitCode;
    }

    /// <summary>
    /// Stops the host (cancelling background services) and always disposes it. With <paramref name="rethrow"/>, a
    /// failure to stop propagates; without it (an exception is already propagating), the failure is logged and
    /// swallowed so it can't replace the original exception.
    /// </summary>
    internal void StopHost(bool rethrow)
    {
        try
        {
            _host.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (!rethrow)
        {
            TryLogStopFailure(exception);
        }
        finally
        {
            if (rethrow)
            {
                _host.Dispose();
            }
            else
            {
                TryDisposeHost();
            }
        }
    }

    // Used only while another exception propagates: nothing here may throw.
    private void TryDisposeHost()
    {
        try
        {
            _host.Dispose();
        }
        catch (Exception exception)
        {
            TryLogStopFailure(exception);
        }
    }

    // Used only while another exception propagates: nothing here may throw.
    private void TryLogStopFailure(Exception exception)
    {
        try
        {
            if (_host.Services?.GetService<ILogger<DesktopApplication>>() is { } logger)
            {
                Log.StopFailed(logger, exception);
            }
        }
        catch
        {
            // The host's logging may be unusable after a failed stop; the original exception matters more.
        }
    }

    [ExcludeFromCodeCoverage]
    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Warning, "{Application} does not implement {Interface}; the service provider was not injected", EventName = "ServiceProviderNotInjected")]
        public static partial void ServiceProviderNotInjected(ILogger logger, string application, string @interface);

        [LoggerMessage(2, LogLevel.Error, "Stopping the host failed while another exception was propagating", EventName = "StopFailed")]
        public static partial void StopFailed(ILogger logger, Exception exception);
    }
}
