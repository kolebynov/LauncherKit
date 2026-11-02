using Avalonia.Controls;
using LauncherKit.Desktop.Services;
using Avalonia.Controls.ApplicationLifetimes;

namespace LauncherKit.Desktop.Avalonia;

/// <summary>Consumer-owned desktop surfaces used by the kit without resolving consumer views.</summary>
public sealed class LauncherKitDesktopOptions
{
	public required Func<Window?> GetMainWindow { get; init; }
	public required Func<IClassicDesktopStyleApplicationLifetime> GetLifetime { get; init; }
	public required Func<IEnumerable<IAppCloseHandler>> GetCloseHandlers { get; init; }
	public required Action RefreshShell { get; init; }
	/// <summary>Rebuilds rendered pages and clears the consumer navigation cache, retaining their models.</summary>
	public required Action ReloadViews { get; init; }
}
