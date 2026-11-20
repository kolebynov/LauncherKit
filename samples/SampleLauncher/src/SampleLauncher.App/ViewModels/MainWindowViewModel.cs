using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Desktop.ViewModels.Setup;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using R3;

namespace SampleLauncher.App.ViewModels;

/// <summary>
/// The pages of the current scope. The home page is the first setup while it has steps; Downloads exists only once the
/// setup is done. A setup that sets the Skyrim directory replaces the scope; otherwise the setup's end shows the pages.
/// </summary>
public sealed partial class MainWindowViewModel : MainWindowViewModelBase
{
	[ObservableProperty]
	public partial ViewModelBase HomePage { get; private set; }

	[ObservableProperty]
	public partial DownloadsViewModel? Downloads { get; private set; }

	public AppTaskQueueViewModel Tasks { get; }

	public MainWindowViewModel(AppTaskQueueViewModel tasks, IServiceProvider services, IOptionsSnapshot<AppSettings> settings)
	{
		Tasks = tasks;
		var firstSetup = services.GetRequiredService<FirstSetupViewModel>();
		if (firstSetup.IsRequired)
		{
			HomePage = firstSetup;
			// Without a Skyrim directory the setup sets one and the kit replaces this scope.
			if (!string.IsNullOrEmpty(settings.Value.SkyrimDirectory))
			{
				firstSetup.ObservePropertyChanged(x => x.State)
					.Where(x => x == FirstSetupState.Finished)
					.Take(1)
					.Subscribe(_ => ShowMainPages(services));
			}
		}
		else
		{
			HomePage = services.GetRequiredService<HomePageViewModel>();
			Downloads = services.GetRequiredService<DownloadsViewModel>();
		}
	}

	private void ShowMainPages(IServiceProvider services)
	{
		HomePage = services.GetRequiredService<HomePageViewModel>();
		Downloads = services.GetRequiredService<DownloadsViewModel>();
	}
}
