using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;
using LauncherKit.Desktop.Services;

namespace SampleLauncher.App.Views;

/// <summary>Yes/no questions (the kit's close confirmation with pending tasks) as a modal window over the main window.</summary>
public sealed class DialogService : IDialogService
{
	public async Task<bool> ShowYesNoConfirmation(string header, string message, bool defaultToNo = false,
		CancellationToken cancellationToken = default)
	{
		var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
			?? throw new InvalidOperationException("The main window is not open.");
		var yes = new Button { Name = "YesButton", Content = "Yes", IsDefault = !defaultToNo };
		var no = new Button { Name = "NoButton", Content = "No", IsDefault = defaultToNo };
		var dialog = new Window
		{
			Title = header,
			SizeToContent = SizeToContent.WidthAndHeight,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Content = new StackPanel
			{
				Margin = new Avalonia.Thickness(16),
				Spacing = 12,
				MaxWidth = 420,
				Children =
				{
					new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
					new StackPanel
					{
						Orientation = Orientation.Horizontal,
						HorizontalAlignment = HorizontalAlignment.Right,
						Spacing = 8,
						Children = { yes, no },
					},
				},
			},
		};
		yes.Click += (_, _) => dialog.Close(true);
		no.Click += (_, _) => dialog.Close(false);
		await using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
		return await dialog.ShowDialog<bool>(owner);
	}
}
