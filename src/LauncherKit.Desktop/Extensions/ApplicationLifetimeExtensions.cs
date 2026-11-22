using System.ComponentModel;

namespace LauncherKit.Desktop.Extensions;

public static class ApplicationLifetimeExtensions
{
	extension (IAvaloniaApplicationLifetime applicationLifetime)
	{
		public IMainView RequiredMainWindow =>
			applicationLifetime.MainWindow ?? throw new InvalidOperationException("Main window is not initialized.");

		/// <summary>
		/// Runs <paramref name="action"/> once, synchronously, when the main window is assigned to the lifetime (before it
		/// is shown), then detaches. Exceptions from <paramref name="action"/> propagate to the code assigning the window.
		/// </summary>
		public void OnMainWindowAssigned(Action<IMainView> action)
		{
			applicationLifetime.PropertyChanged += OnPropertyChanged;

			void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
			{
				if (e.PropertyName != nameof(IAvaloniaApplicationLifetime.MainWindow))
				{
					return;
				}

				applicationLifetime.PropertyChanged -= OnPropertyChanged;
				action(applicationLifetime.RequiredMainWindow);
			}
		}
	}
}
