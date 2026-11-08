using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using LauncherKit.Desktop.Services;
using LauncherKit.Infrastructure;
using HandleResult = LauncherKit.Desktop.Services.IAppCloseHandler.HandleResult;

namespace LauncherKit.Desktop.Tests.Services;

[NonParallelizable]
[TestFixture]
public sealed class NavigationGuardTests
{
	[AvaloniaTest]
	[TestCase(true)]
	[TestCase(false)]
	public async Task Navigate_CurrentContentIsGuard_NavigatesOnlyWhenAllowed(bool canNavigate)
	{
		// Arrange
		var view = new MainViewWindow { CurrentContent = new StubGuard(() => ValueTask.FromResult(canNavigate)) };
		var navigations = 0;

		// Act
		await new NavigationGuard(view).Navigate(() => navigations++);

		// Assert
		Assert.That(navigations, Is.EqualTo(canNavigate ? 1 : 0));
	}

	[AvaloniaTest]
	public async Task Navigate_CurrentContentIsNotGuard_Navigates()
	{
		// Arrange
		var view = new MainViewWindow { CurrentContent = new object() };
		var navigations = 0;

		// Act
		await new NavigationGuard(view).Navigate(() => navigations++);

		// Assert
		Assert.That(navigations, Is.EqualTo(1));
	}

	[AvaloniaTest]
	public async Task Navigate_WhileGuardAsks_SkipsLaterNavigationAndAllowsAfterwards()
	{
		// Arrange
		var answer = new TaskCompletionSource<bool>();
		var guard = new StubGuard(() => new ValueTask<bool>(answer.Task));
		var navigationGuard = new NavigationGuard(new MainViewWindow { CurrentContent = guard });
		var navigations = new List<string>();
		var first = navigationGuard.Navigate(() => navigations.Add("first"));

		// Act
		await navigationGuard.Navigate(() => navigations.Add("second"));
		answer.SetResult(true);
		await first;
		await navigationGuard.Navigate(() => navigations.Add("third"));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(navigations, Is.EqualTo(new[] { "first", "third" }));
			Assert.That(guard.CallCount, Is.EqualTo(2));
		});
	}

	[AvaloniaTest]
	public async Task Navigate_RequestedWhileNavigating_IsSkipped()
	{
		// Arrange
		var navigationGuard = new NavigationGuard(new MainViewWindow());
		var navigations = new List<string>();
		Task? inner = null;

		// Act
		await navigationGuard.Navigate(() =>
		{
			navigations.Add("outer");
			inner = navigationGuard.Navigate(() => navigations.Add("inner")).AsTask();
		});
		await inner!;

		// Assert
		Assert.That(navigations, Is.EqualTo(new[] { "outer" }));
	}

	[AvaloniaTest]
	public async Task Navigate_GuardThrows_PropagatesAndAllowsLaterNavigation()
	{
		// Arrange
		var failing = true;
		var view = new MainViewWindow
		{
			CurrentContent = new StubGuard(() => failing
				? ValueTask.FromException<bool>(new InvalidOperationException("guard failed"))
				: ValueTask.FromResult(true)),
		};
		var navigationGuard = new NavigationGuard(view);
		var navigations = 0;

		// Act
		var failure = await TryNavigate(navigationGuard, () => navigations++);
		failing = false;
		await navigationGuard.Navigate(() => navigations++);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(failure, Is.InstanceOf<InvalidOperationException>());
			Assert.That(navigations, Is.EqualTo(1));
		});
	}

	private static async Task<Exception?> TryNavigate(NavigationGuard navigationGuard, Action navigate)
	{
		try
		{
			await navigationGuard.Navigate(navigate);
			return null;
		}
		catch (InvalidOperationException e)
		{
			return e;
		}

	}

	[AvaloniaTest]
	[TestCase(true, typeof(HandleResult.CanClose))]
	[TestCase(false, typeof(HandleResult.CannotClose))]
	public async Task CloseHandler_CurrentContentIsGuard_GuardDecides(bool canNavigate, Type expected)
	{
		// Arrange
		var window = new MainViewWindow { CurrentContent = new StubGuard(() => ValueTask.FromResult(canNavigate)) };

		// Act
		var result = await NavigationGuardCloseHandler.Instance.Handle(window);

		// Assert
		Assert.That(result.Value, Is.InstanceOf(expected));
	}

	[AvaloniaTest]
	public async Task CloseHandler_CurrentContentIsNotGuard_CanClose()
	{
		// Arrange
		var window = new MainViewWindow { CurrentContent = new object() };

		// Act
		var result = await NavigationGuardCloseHandler.Instance.Handle(window);

		// Assert
		Assert.That(result.Value, Is.InstanceOf<HandleResult.CanClose>());
	}

	private sealed class MainViewWindow : Window, IMainView
	{
		public object? CurrentContent { get; init; }

		public void SetNavigationGuard(Func<Action, ValueTask> navigate) =>
			throw new AssertionException("No navigation guard expected.");
	}

	private sealed class StubGuard(Func<ValueTask<bool>> canNavigate) : INavigationGuard
	{
		public int CallCount { get; private set; }

		public ValueTask<bool> CanNavigate()
		{
			CallCount++;
			return canNavigate();
		}
	}
}
