using System.Collections;
using LauncherKit.Core.Feed.Mods;

namespace LauncherKit.Core.Tests.TestSupport;

/// <summary>
/// Feed mods that run <paramref name="onEnumerate"/>, with the 1-based number of the enumeration, each time an
/// enumeration starts, to observe or interrupt code that searches them.
/// </summary>
internal sealed class EnumerationHookList(IReadOnlyList<ModFeedData> mods, Action<int> onEnumerate) : IReadOnlyList<ModFeedData>
{
	private int _enumerations;

	public int Count => mods.Count;

	public ModFeedData this[int index] => mods[index];

	public IEnumerator<ModFeedData> GetEnumerator()
	{
		onEnumerate(Interlocked.Increment(ref _enumerations));
		return mods.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
