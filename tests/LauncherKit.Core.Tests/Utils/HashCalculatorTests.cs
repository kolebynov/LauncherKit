using LauncherKit.Core.Utils;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Utils;

[TestFixture]
public sealed class HashCalculatorTests
{
	[Test]
	public async Task HashFile_KnownContent_ReturnsExpectedSha256()
	{
		// Arrange
		using var temp = new TempDirectory();
		var path = temp.CreateFile("input.txt", "abc");
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<HashCalculator>();

		// Act
		var result = await sut.HashFile(path, null, CancellationToken.None);

		// Assert
		Assert.That(result.ToString(), Is.EqualTo("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
	}

	[Test]
	public async Task HashFile_LargeFile_ReportsMonotonicProgressEndingAtOne()
	{
		// Arrange
		using var temp = new TempDirectory();
		var path = temp.CreateFile("large.bin", new byte[6 * 1024 * 1024]);
		var progress = new List<double>();
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<HashCalculator>();

		// Act
		await sut.HashFile(path, value =>
		{
			progress.Add(value);
			return ValueTask.CompletedTask;
		}, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(progress, Is.Not.Empty);
			Assert.That(progress, Is.Ordered);
			Assert.That(progress[^1], Is.EqualTo(1.0));
		});
	}

	[Test]
	public void HashFile_CancelledDuringProgress_ThrowsOperationCanceledException()
	{
		// Arrange
		using var temp = new TempDirectory();
		var path = temp.CreateFile("large.bin", new byte[9 * 1024 * 1024]);
		using var cancellation = new CancellationTokenSource();
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<HashCalculator>();

		// Act
		// Assert
		Assert.That(async () => await sut.HashFile(path, _ =>
		{
			cancellation.Cancel();
			return ValueTask.CompletedTask;
		}, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
	}
}
