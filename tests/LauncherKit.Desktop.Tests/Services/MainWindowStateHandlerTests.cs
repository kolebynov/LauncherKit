using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LauncherKit.Desktop.Tests.Services;

[NonParallelizable]
[TestFixture]
public sealed class MainWindowStateHandlerTests
{
	[AvaloniaTest]
	[TestCase(true, WindowState.Maximized)]
	[TestCase(false, WindowState.Normal)]
	public void Initialize_MainWindowAssigned_AppliesSavedBoundsAndState(bool isMaximized, WindowState expectedState)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var settings = CreateSettings();
		settings.Window.IsMaximized = isMaximized;
		mocker.GetMock<ISettingsService>().Setup(s => s.GetSettings()).Returns(settings);
		var window = new MainViewWindow();
		mocker.GetMock<IAvaloniaApplicationLifetime>().SetupGet(x => x.MainWindow).Returns(window);
		CreateHandler(mocker).Initialize();

		// Act
		mocker.GetMock<IAvaloniaApplicationLifetime>().Raise(x => x.PropertyChanged += null,
			new PropertyChangedEventArgs(nameof(IAvaloniaApplicationLifetime.MainWindow)));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(window.Width, Is.EqualTo(900));
			Assert.That(window.Height, Is.EqualTo(600));
			Assert.That(window.Position, Is.EqualTo(new PixelPoint(10, 20)));
			Assert.That(window.WindowState, Is.EqualTo(expectedState));
		});
	}

	[Test]
	public void OnMainWindowAssigned_ActionThrows_PropagatesToRaiserAndRunsOnce()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var lifetime = mocker.GetMock<IAvaloniaApplicationLifetime>();
		var mainView = mocker.Get<IMainView>();
		lifetime.SetupGet(x => x.MainWindow).Returns(mainView);
		var calls = new List<IMainView>();
		lifetime.Object.OnMainWindowAssigned(view =>
		{
			calls.Add(view);
			throw new InvalidOperationException("restore failed");
		});
		var args = new PropertyChangedEventArgs(nameof(IAvaloniaApplicationLifetime.MainWindow));

		// Act
		lifetime.Raise(x => x.PropertyChanged += null, new PropertyChangedEventArgs("Other"));
		var exception = Assert.Throws<InvalidOperationException>(() => lifetime.Raise(x => x.PropertyChanged += null, args));
		lifetime.Raise(x => x.PropertyChanged += null, args);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(exception!.Message, Is.EqualTo("restore failed"));
			Assert.That(calls, Is.EqualTo(new[] { mainView }));
		});
	}

	[AvaloniaTest]
	[TestCase(WindowState.Normal, 1100, 750, 30, 40, false)]
	[TestCase(WindowState.Maximized, 900, 600, 10, 20, true)]
	public async Task Handle_NotMinimized_SavesMaximizedAndOnlyNormalBounds(WindowState state, double width, double height,
		int x, int y, bool isMaximized)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var settings = CreateSettings();
		mocker.GetMock<ISettingsService>().Setup(s => s.ModifySettings(It.IsAny<Func<AllSettings, ValueTask>>()))
			.Returns(async (Func<AllSettings, ValueTask> modify) => await modify(settings));
		var window = CreateWindow(state);

		// Act
		var result = await CreateHandler(mocker).Handle(window);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.InstanceOf<IAppCloseHandler.HandleResult.CanClose>());
			Assert.That(settings.Window.Width, Is.EqualTo(width));
			Assert.That(settings.Window.Height, Is.EqualTo(height));
			Assert.That(settings.Window.PositionX, Is.EqualTo(x));
			Assert.That(settings.Window.PositionY, Is.EqualTo(y));
			Assert.That(settings.Window.IsMaximized, Is.EqualTo(isMaximized));
		});
	}

	[AvaloniaTest]
	public async Task Handle_Minimized_SavesNothing()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var window = CreateWindow(WindowState.Minimized);

		// Act
		var result = await CreateHandler(mocker).Handle(window);

		// Assert
		Assert.That(result.Value, Is.InstanceOf<IAppCloseHandler.HandleResult.CanClose>());
		mocker.GetMock<ISettingsService>().Verify(s => s.ModifySettings(It.IsAny<Func<AllSettings, ValueTask>>()),
			Times.Never);
	}

	private static MainWindowStateHandler CreateHandler(AutoMocker mocker)
	{
		var services = new ServiceCollection().AddSingleton(mocker.Get<ISettingsService>()).BuildServiceProvider();
		mocker.GetMock<IAvaloniaApplicationLifetime>().SetupGet(x => x.Services).Returns(services);
		return mocker.CreateInstance<MainWindowStateHandler>();
	}

	private static AllSettings CreateSettings() => new()
	{
		App = new AppSettings(), Download = new DownloadSettings(),
		Window = new WindowSettings { Width = 900, Height = 600, PositionX = 10, PositionY = 20 },
	};

	private static Window CreateWindow(WindowState state)
	{
		var window = new Window { Width = 1100, Height = 750, Position = new PixelPoint(30, 40) };
		window.Show();
		window.WindowState = state;
		return window;
	}

	private sealed class MainViewWindow : Window, IMainView
	{
		public object? CurrentContent => null;

		public void SetNavigationGuard(Func<Action, ValueTask> navigate) =>
			throw new AssertionException("No navigation guard expected.");

		public void ReloadViews() => throw new AssertionException("No view reload expected.");
	}
}
