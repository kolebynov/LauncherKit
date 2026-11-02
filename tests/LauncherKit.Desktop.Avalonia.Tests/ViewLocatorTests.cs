using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using LauncherKit.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LauncherKit.Desktop.Avalonia.Tests;

[NonParallelizable]
[TestFixture]
public sealed class ViewLocatorTests
{
	[AvaloniaTest]
	public void GetPageFromObject_ScopeReplacedBetweenPages_BuildsFromCurrentScopeAndAssignsViewModel()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var model = new TestViewModel();
		using var firstServices = new ServiceCollection().BuildServiceProvider();
		using var secondServices = new ServiceCollection().BuildServiceProvider();
		var first = new TextBlock();
		var second = new TextBlock();
		mocker.GetMock<IViewRegistry>().Setup(x => x.CreateView(model, firstServices)).Returns(first);
		mocker.GetMock<IViewRegistry>().Setup(x => x.CreateView(model, secondServices)).Returns(second);
		IServiceProvider currentServices = firstServices;
		var factory = new FramePageFactory(new ViewLocator(mocker.GetMock<IViewRegistry>().Object, () => currentServices));

		// Act
		var firstPage = factory.GetPageFromObject(model);
		currentServices = secondServices;
		var secondPage = factory.GetPageFromObject(model);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(firstPage, Is.SameAs(first));
			Assert.That(secondPage, Is.SameAs(second));
			Assert.That(firstPage.DataContext, Is.SameAs(model));
			Assert.That(secondPage.DataContext, Is.SameAs(model));
		});
	}

	[AvaloniaTest]
	public void Build_NotViewModel_ThrowsInsteadOfAskingRegistry()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var locator = new ViewLocator(mocker.GetMock<IViewRegistry>().Object, () => throw new AssertionException("No scope expected."));

		// Act & Assert
		Assert.Multiple(() =>
		{
			Assert.That(locator.Build(null), Is.Null);
			Assert.That(() => locator.Build("text"), Throws.ArgumentException);
			Assert.That(locator.Match("text"), Is.False);
			Assert.That(locator.Match(new TestViewModel()), Is.True);
		});
	}

	[AvaloniaTest]
	public void GetPage_PageType_ThrowsNotSupported()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var factory = new FramePageFactory(new ViewLocator(mocker.GetMock<IViewRegistry>().Object, () => throw new AssertionException("No scope expected.")));

		// Act & Assert
		Assert.That(() => factory.GetPage(typeof(UserControl)), Throws.TypeOf<NotSupportedException>());
	}

	private sealed class TestViewModel : ViewModelBase;
}
