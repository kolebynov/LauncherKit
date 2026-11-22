using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LauncherKit.Desktop;

internal sealed class AvaloniaApplicationLifetime : IAvaloniaApplicationLifetime, IDisposable
{
	private readonly IClassicDesktopStyleApplicationLifetime _avaloniaLifetime;
	private readonly CancellationTokenSource _mainWindowOpenedCts = new();

	public IMainView? MainWindow
	{
		get => field;
		set
		{
			if (field != null)
			{
				throw new InvalidOperationException("Main window is already set.");
			}

			field = value ?? throw new InvalidOperationException("Main window cannot be null.");

			field.AsWindow().Opened += OnOpened;

			void OnOpened(object? sender, EventArgs e)
			{
				field.AsWindow().Opened -= OnOpened;
				ActionUtils.ExecuteAndSupressError(_mainWindowOpenedCts.Cancel);
			}
		}
	}

	public CancellationToken MainWindowOpened => _mainWindowOpenedCts.Token;

	public IServiceProvider Services => ((LauncherKitApp)Application.Current!).Services;

	public AvaloniaApplicationLifetime(IClassicDesktopStyleApplicationLifetime avaloniaLifetime)
	{
		_avaloniaLifetime = avaloniaLifetime;
	}

	public void ReloadViews()
	{
		MainWindow?.ReloadViews();
	}

	public void CreateNewScope()
	{
		((LauncherKitApp)Application.Current!).AppInstance.CreateNewScope();
		MainWindow?.AsWindow().DataContext = Services.GetRequiredService<MainWindowViewModelBase>();
	}

	public void Exit()
	{
		_avaloniaLifetime.Shutdown();
	}

	public void Dispose()
	{
		_mainWindowOpenedCts.Dispose();
	}
}