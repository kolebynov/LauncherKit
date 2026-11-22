using Avalonia.Markup.Xaml;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Desktop;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SampleLauncher.App.ViewModels;
using SampleLauncher.App.Views;
using AppBuilder = LauncherKit.Core.AppBuilder;
#if DEV_AUTOMATION
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
#endif

namespace SampleLauncher.App;

public sealed class App : LauncherKitApp
{
	// A supported language skips the setup's language step.
	protected override IEnumerable<KeyValuePair<string, string?>> GetInMemoryConfiguration() => [new("App:Language", "en")];

	protected override void LoadXaml() => AvaloniaXamlLoader.Load(this);

	protected override void ConfigureServices(AppBuilder builder)
	{
		var services = builder.Services;
		services.AddSingleton<ILaunchersRootDirectoryManager>(sp => new SampleLaunchersRootDirectoryManager(true,
			sp.GetRequiredService<IFileSystemUtils>(), sp.GetRequiredService<IRegistryManager>(),
			sp.GetRequiredService<IOptions<LauncherProductOptions>>().Value));
		// Replaces the kit's Windows finder: the sample must never suggest a real Skyrim installation.
		services.AddSingleton<ISkyrimDirectoryFinder, SampleSkyrimDirectoryFinder>();
		services.AddSingleton<IViewRegistry, SampleViewRegistry>();
		services.AddScoped<MainWindowViewModelBase, MainWindowViewModel>();
		services.AddScoped<HomePageViewModel>();
		services.AddSingleton<INotificationManagerProvider>(sp =>
			(MainWindow)sp.GetRequiredService<IAvaloniaApplicationLifetime>().RequiredMainWindow);
		services.AddSingleton<IDialogService, DialogService>();
		services.AddLaunchHandler<SampleLaunchHandler>();
	}

	protected override IMainView CreateMainView() => new MainWindow(
		Services.GetRequiredService<IOptions<LauncherProductOptions>>(), Services.GetRequiredService<IAppVersionProvider>());

#if DEV_AUTOMATION
	protected override IDisposable? StartDesktopServices(IClassicDesktopStyleApplicationLifetime desktop) =>
		LauncherKit.DevAutomation.DevAutomationServer.Start(
			Services.GetRequiredService<IConfiguration>(), desktop, () => Services,
			Services.GetRequiredService<ILoggerFactory>());
#endif
}
