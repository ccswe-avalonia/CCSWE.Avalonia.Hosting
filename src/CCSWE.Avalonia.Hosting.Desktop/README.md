# CCSWE.Avalonia.Hosting.Desktop

Bootstrap an Avalonia classic-desktop app (Windows, Linux, macOS) on the .NET Generic Host — DI, hosted-service
lifecycle, configuration, and logging — without the hand-written Avalonia-to-host plumbing.

```csharp
[STAThread]
public static void Main(string[] args)
{
    var builder = DesktopApplication.CreateBuilder<App>(args);
    builder.Services.AddSingleton<IMyService, MyService>();
    builder.Services.AddHostedService<MyBackgroundWorker>();
    builder.Build().Run(args);
}

// The Avalonia XAML previewer needs a parameterless AppBuilder-returning method.
public static AppBuilder BuildAvaloniaApp() => DesktopApplication.ConfigureAppBuilder<App>();
```

`Run()` starts the hosted services, runs the Avalonia main loop, and stops the host on exit (cancelling
background services). When the host stops while the UI runs — Ctrl+C or SIGTERM, `IHostApplicationLifetime.StopApplication()`, or a
`BackgroundService` that throws (the default `BackgroundServiceExceptionBehavior.StopHost`; set it to `Ignore` to keep
the app running) — `Run()` closes the UI with a forced shutdown. `DesktopApplicationOptions.SuppressStatusMessages`
(default `true`) hides the console lifetime's "Press Ctrl+C to shut down." status lines. Reference `Avalonia.Desktop`
at the same version as `Avalonia`; Avalonia's platform packages aren't compatible across versions. Your `App` implements `IServiceProviderAccessor` (from `CCSWE.Avalonia.Hosting`) to receive
the built provider before `OnFrameworkInitializationCompleted`. All types share the `CCSWE.Avalonia.Hosting`
namespace, so a single `using CCSWE.Avalonia.Hosting;` is all you need.

See the [repository](https://github.com/ccswe-avalonia/CCSWE.Avalonia.Hosting) for full usage.

MIT © Cory Charlton / CCSWE.
