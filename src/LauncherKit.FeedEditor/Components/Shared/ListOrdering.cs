namespace LauncherKit.FeedEditor.Components.Shared;

/// <summary>Positions in a list shown as several groups, while the list itself stays the only stored order.</summary>
public static class ListOrdering
{
	/// <summary>
	/// The index of the nearest entry in <paramref name="direction"/> (-1 or 1) from <paramref name="index"/> that matches
	/// <paramref name="filter"/>, or -1 when there is none.
	/// </summary>
	public static int NeighborIndex<T>(IReadOnlyList<T> items, int index, int direction, Func<T, bool> filter)
	{
		for (var i = index + direction; i >= 0 && i < items.Count; i += direction)
		{
			if (filter(items[i]))
			{
				return i;
			}
		}

		return -1;
	}

	/// <summary>The index right after the last entry matching <paramref name="filter"/>, or the list end when none match.</summary>
	public static int InsertAfterLastIndex<T>(IReadOnlyList<T> items, Func<T, bool> filter)
	{
		for (var i = items.Count - 1; i >= 0; i--)
		{
			if (filter(items[i]))
			{
				return i + 1;
			}
		}

		return items.Count;
	}
}
