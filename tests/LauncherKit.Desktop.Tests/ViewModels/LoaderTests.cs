using LauncherKit.Desktop.ViewModels;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class LoaderTests
{
	[Test]
	public void BeginLoading_OverlappingScopes_NotifiesOnlyWhenLoadingStartsAndFinishes()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		var sut = mocker.CreateInstance<Loader>();
		var states = new List<bool>();
		sut.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(Loader.IsLoading))
			{
				states.Add(sut.IsLoading);
			}
		};

		using var first = sut.BeginLoading();
		using var second = sut.BeginLoading();
		first.Dispose();

		Assert.That(sut.IsLoading, Is.True, "The newer load is still running.");
		Assert.That(states, Is.EqualTo(new[] { true }));

		second.Dispose();

		Assert.That(sut.IsLoading, Is.False);
		Assert.That(states, Is.EqualTo(new[] { true, false }));
	}

	[Test]
	public void BeginLoading_DisposingCompletedScopeAgain_DoesNotFinishAnotherLoad()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		var sut = mocker.CreateInstance<Loader>();
		using var completed = sut.BeginLoading();
		completed.Dispose();

		using var current = sut.BeginLoading();
		completed.Dispose();

		Assert.That(sut.IsLoading, Is.True);

		current.Dispose();

		Assert.That(sut.IsLoading, Is.False);
	}
}
