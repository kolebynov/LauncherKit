using LauncherKit.Core.Models;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Models;

[TestFixture]
public sealed class BaseAsyncResultTests
{
	[Test]
	public async Task SetResult_FirstResult_CompletesAwaiterAndIgnoresLaterResults()
	{
		using var sut = new TestAsyncResult(CancellationToken.None);

		sut.SetResult(42);
		sut.SetResult(99);

		Assert.That(await TaskAssert.WithinTimeout(sut.ResultTask), Is.EqualTo(42));
	}

	[Test]
	public void Cancellation_CancellableToken_CancelsResultTask()
	{
		using var cancellation = new CancellationTokenSource();
		using var sut = new TestAsyncResult(cancellation.Token);

		cancellation.Cancel();

		Assert.That(async () => await sut.ResultTask, Throws.InstanceOf<OperationCanceledException>());
	}

	private sealed class TestAsyncResult(CancellationToken cancellationToken) : BaseAsyncResult<int>(cancellationToken);
}

[TestFixture]
public sealed class InstallContextTests
{
	[Test]
	public void GetContextForStep_SameAndOtherIndex_ReturnsStableIndependentContexts()
	{
		var sut = new InstallContext();

		var first = sut.GetContextForStep(0);
		var same = sut.GetContextForStep(0);
		var second = sut.GetContextForStep(1);

		Assert.Multiple(() =>
		{
			Assert.That(same, Is.SameAs(first));
			Assert.That(second, Is.Not.SameAs(first));
		});
	}
}

[TestFixture]
public sealed class ModInfoTests
{
	[Test]
	public void DescriptionAndVersion_RepeatedReads_EvaluateMetadataOnce()
	{
		var evaluations = 0;
		var sut = new ModInfo("Example", new Lazy<MetaIniData?>(() =>
		{
			evaluations++;
			return new MetaIniData { Comments = "Description", Version = "2.1.0" };
		}));

		Assert.Multiple(() =>
		{
			Assert.That(sut.Description, Is.EqualTo("Description"));
			Assert.That(sut.Version, Is.EqualTo("2.1.0"));
			Assert.That(evaluations, Is.EqualTo(1));
		});
	}
}
