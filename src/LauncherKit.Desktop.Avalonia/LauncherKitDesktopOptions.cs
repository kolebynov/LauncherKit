using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace LauncherKit.Desktop.Avalonia;

/// <summary>Consumer-owned desktop surfaces used by the kit without resolving consumer views.</summary>
public sealed class LauncherKitDesktopOptions
{
	public required Func<Window?> GetMainWindow { get; init; }
	public required Func<IClassicDesktopStyleApplicationLifetime> GetLifetime { get; init; }
	/// <summary>The services of the application scope current at the call; the scope is replaced on settings changes.</summary>
	public required Func<IServiceProvider> GetCurrentServices { get; init; }
	/// <summary>Sets the main window's data context to the current scope's <c>MainWindowViewModelBase</c>.</summary>
	public required Action RefreshMainWindow { get; init; }
	/// <summary>Rebuilds rendered pages and clears the consumer navigation cache, retaining their models.</summary>
	public required Action ReloadViews { get; init; }
}
