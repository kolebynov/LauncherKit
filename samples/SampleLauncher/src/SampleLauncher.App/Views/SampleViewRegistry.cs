using Avalonia.Controls;
using LauncherKit.Desktop;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Desktop.ViewModels.Setup;
using SampleLauncher.App.ViewModels;

namespace SampleLauncher.App.Views;

public sealed class SampleViewRegistry : IViewRegistry
{
	public Control CreateView(ViewModelBase viewModel, IServiceProvider services) => viewModel switch
	{
		FirstSetupViewModel => new FirstSetupView(),
		DirectorySetupStepViewModel => new DirectorySetupStepView(),
		AvailabilitySetupStepViewModel => new AvailabilitySetupStepView(),
		CheckAvailabilityViewModel => new CheckAvailabilityView(),
		HomePageViewModel => new HomeView(),
		ModCategoriesViewModel => new ModCategoriesView(),
		AppTaskQueueViewModel => new AppTaskQueueView(),
		DownloadsViewModel => new DownloadsView(),
		_ => throw new InvalidOperationException($"No view is registered for view model '{viewModel.GetType().FullName}'."),
	};
}
