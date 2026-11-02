using R3;
using LauncherKit.Desktop.Activation;

namespace LauncherKit.Desktop.Tests.Activation;

[TestFixture]
public sealed class ViewModelActivatorTests
{
	[Test]
	public async Task Activate_NestedHandles_DisposesActivationResourcesAfterLastHandleOnce()
	{
		// Arrange
		var disposeCount = 0;
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<ViewModelActivator>();
		sut.AddActivationBlock((disposables, _) =>
		{
			disposables.Add(Disposable.Create(() => disposeCount++));
			return ValueTask.CompletedTask;
		});

		// Act
		var first = await sut.Activate(CancellationToken.None);
		var second = await sut.Activate(CancellationToken.None);
		first.Dispose();
		var afterFirstHandle = disposeCount;
		second.Dispose();
		sut.Dispose();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(afterFirstHandle, Is.Zero);
			Assert.That(disposeCount, Is.EqualTo(1));
		});
	}
}
