using Avalonia.Controls.ApplicationLifetimes;
using IApplicationLifetime = LauncherKit.Core.Services.Abstractions.IApplicationLifetime;

namespace LauncherKit.Desktop.Avalonia;

internal sealed class ApplicationLifetime : IApplicationLifetime
{
	private readonly IClassicDesktopStyleApplicationLifetime _avaloniaLifetime;

	public ApplicationLifetime(IClassicDesktopStyleApplicationLifetime avaloniaLifetime)
	{
		_avaloniaLifetime = avaloniaLifetime;
	}

	public void Exit()
	{
		_avaloniaLifetime.Shutdown();
	}
}