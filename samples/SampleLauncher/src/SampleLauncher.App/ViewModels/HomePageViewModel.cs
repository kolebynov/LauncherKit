using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LauncherKit.Core.AppTasks;
using LauncherKit.Desktop.ViewModels;

namespace SampleLauncher.App.ViewModels;

/// <summary>The feed's mod categories and the sample download through the kit task queue.</summary>
public sealed partial class HomePageViewModel(ModCategoriesViewModel modCategories, IAppTaskQueue appTaskQueue)
	: ViewModelBase
{
	public ModCategoriesViewModel ModCategories { get; } = modCategories;

	/// <summary>Set once the sample download task completed in this scope.</summary>
	[ObservableProperty]
	public partial bool IsSampleDownloaded { get; private set; }

	// A failed download stays in the task queue (Tasks page) until resumed or cancelled.
	[RelayCommand]
	private async Task DownloadSample(CancellationToken cancellationToken)
	{
		IsSampleDownloaded = false;
		await appTaskQueue.EnqueueAndWaitForCompletion(SampleDownload.CreateTask(), cancellationToken);
		IsSampleDownloaded = true;
	}
}
