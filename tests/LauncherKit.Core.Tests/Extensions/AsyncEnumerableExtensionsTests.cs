using System.Runtime.CompilerServices;
using LauncherKit.Core.Extensions;

namespace LauncherKit.Core.Tests.Extensions;

[TestFixture]
public sealed class AsyncEnumerableExtensionsTests
{
	[Test]
	public async Task Enumerate_SourceWithSideEffects_ConsumesEveryItem()
	{
		var consumed = new List<int>();

		await Values(consumed).Enumerate(CancellationToken.None);

		Assert.That(consumed, Is.EqualTo(new[] { 1, 2, 3 }));
	}

	[Test]
	public void Enumerate_CancelledToken_PropagatesCancellation()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		Assert.That(async () => await Values([]).Enumerate(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
	}

	private static async IAsyncEnumerable<int> Values(
		ICollection<int> consumed, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		foreach (var value in new[] { 1, 2, 3 })
		{
			cancellationToken.ThrowIfCancellationRequested();
			consumed.Add(value);
			yield return value;
			await Task.Yield();
		}
	}
}
