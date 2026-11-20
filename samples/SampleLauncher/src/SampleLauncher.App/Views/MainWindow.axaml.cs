using Avalonia.Controls;
using Avalonia.Interactivity;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Desktop;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Options;
using R3;
using SampleLauncher.App.ViewModels;

namespace SampleLauncher.App.Views;

/// <summary>
/// A plain window: navigation buttons and the current page, rendered through the kit's view locator. Every navigation
/// goes through the kit's guard. A new data context (a replaced scope) or home page shows the home page.
/// </summary>
public partial class MainWindow : Window, IMainView
{
	private Func<Action, ValueTask>? _navigationGuard;

	public MainWindow()
	{
		InitializeComponent();
	}

	public MainWindow(IOptions<LauncherProductOptions> product, IAppVersionProvider appVersionProvider) : this()
	{
		Title = $"{product.Value.DisplayName} {appVersionProvider.Version}";
		IDisposable? homePageSubscription = null;
		DataContextChanged += (_, _) =>
		{
			homePageSubscription?.Dispose();
			homePageSubscription = (DataContext as MainWindowViewModel)?
				.ObservePropertyChanged(x => x.HomePage)
				.Subscribe(page => _ = Navigate(page).AsTask());
		};
	}

	public object? CurrentContent => PageHost.Content;

	public void SetNavigationGuard(Func<Action, ValueTask> navigate) => _navigationGuard = navigate;

	/// <summary>Recreates the current page's view, keeping its view model.</summary>
	public void ReloadViews()
	{
		var content = PageHost.Content;
		PageHost.Content = null;
		PageHost.Content = content;
	}

	private ValueTask Navigate(ViewModelBase? page)
	{
		if (page is null) return ValueTask.CompletedTask;
		if (_navigationGuard is null)
		{
			PageHost.Content = page;
			return ValueTask.CompletedTask;
		}

		return _navigationGuard(() => PageHost.Content = page);
	}

	private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

	private async void HomeNavButtonOnClick(object? sender, RoutedEventArgs e) => await Navigate(ViewModel?.HomePage);

	private async void TasksNavButtonOnClick(object? sender, RoutedEventArgs e) => await Navigate(ViewModel?.Tasks);

	private async void DownloadsNavButtonOnClick(object? sender, RoutedEventArgs e) => await Navigate(ViewModel?.Downloads);
}
