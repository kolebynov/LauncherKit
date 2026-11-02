using LauncherKit.Core.Exceptions;

namespace LauncherKit.Core.Services;

/// <summary>
/// Merges Mod Organizer plugin lists. A <c>plugins.txt</c> line is a plugin, active when prefixed with <c>*</c>, or a
/// comment starting with <c>#</c>. <c>loadorder.txt</c> lists plugins without a state: the primary plugins the game
/// loads first (which <c>plugins.txt</c> does not list), then the plugins of <c>plugins.txt</c> in its order. Plugin
/// names are file names, so they are compared case-insensitively.
/// </summary>
internal static class PluginListMerger
{
	private const char ActiveMarker = '*';
	private const char CommentMarker = '#';

	private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

	/// <summary>
	/// Merges the plugins of <paramref name="mergedLines"/> (a possibly partial <c>plugins.txt</c>) into
	/// <paramref name="currentLines"/>, the profile's <c>plugins.txt</c>. The merged plugins end up in their order in
	/// <paramref name="mergedLines"/> and with their state there; plugins it does not list keep their places. The
	/// largest set of merged plugins that are already listed in the right relative order stays in place (of equally
	/// large sets, the one whose plugins are listed later), and every other merged plugin, moved or new, goes right
	/// after the plugin preceding it in <paramref name="mergedLines"/> (the first ones before the first plugin that
	/// stayed, or at the end when none did). Moving plugins later rather than earlier keeps them after the masters
	/// they may have among the plugins in between. The current comments stay at the top and blank lines are dropped.
	/// A listed plugin keeps the letter case of its current line.
	/// </summary>
	/// <exception cref="DuplicatePluginException"><paramref name="mergedLines"/> lists a plugin twice.</exception>
	public static PluginsMergeResult MergePlugins(IReadOnlyList<string> currentLines, IReadOnlyList<string> mergedLines)
	{
		var current = ParsePlugins(currentLines);
		var merged = ParsePlugins(mergedLines);
		var duplicate = merged
			.GroupBy(plugin => plugin.Name, NameComparer)
			.FirstOrDefault(group => group.Skip(1).Any());
		if (duplicate != null)
		{
			throw new DuplicatePluginException(duplicate.Key);
		}

		var currentIndices = new Dictionary<string, int>(current.Count, NameComparer);
		for (var i = 0; i < current.Count; i++)
		{
			currentIndices.TryAdd(current[i].Name, i);
		}

		var currentPositions = merged
			.Select(plugin => currentIndices.GetValueOrDefault(plugin.Name, -1))
			.ToArray();
		var stays = FindLongestIncreasingSubsequence(currentPositions);
		var movedNames = merged
			.Where((_, i) => currentPositions[i] >= 0 && !stays[i])
			.Select(plugin => plugin.Name)
			.ToHashSet(NameComparer);
		var result = current.Where(plugin => !movedNames.Contains(plugin.Name)).ToList();

		var firstStayingIndex = Array.IndexOf(stays, true);
		var lastIndex = -1;
		var changedStateCount = 0;
		for (var i = 0; i < merged.Count; i++)
		{
			var plugin = merged[i];
			if (currentPositions[i] >= 0 && current[currentPositions[i]].IsActive != plugin.IsActive)
			{
				changedStateCount++;
			}

			if (stays[i])
			{
				lastIndex = result.FindIndex(lastIndex + 1, x => NameComparer.Equals(x.Name, plugin.Name));
				result[lastIndex] = result[lastIndex] with { IsActive = plugin.IsActive };
				continue;
			}

			lastIndex = (i, firstStayingIndex) switch
			{
				(> 0, _) => lastIndex + 1,
				(_, < 0) => result.Count,
				_ => result.FindIndex(x => NameComparer.Equals(x.Name, merged[firstStayingIndex].Name)),
			};
			var name = currentPositions[i] >= 0 ? current[currentPositions[i]].Name : plugin.Name;
			result.Insert(lastIndex, plugin with { Name = name });
		}

		var lines = ToLines(currentLines, result);
		var addedCount = currentPositions.Count(position => position < 0);
		return new PluginsMergeResult(lines, addedCount, movedNames.Count, changedStateCount);
	}

	/// <summary>
	/// Undoes <see cref="MergePlugins"/> of <paramref name="mergedLines"/> into <paramref name="previousLines"/>, keeping
	/// what changed since in <paramref name="currentLines"/>, the profile's current <c>plugins.txt</c>. The plugins the
	/// merge did not list are the current ones, with their current state and in their current order, which never
	/// changes. The merged plugins get back their previous state and relative order, each going right after the last
	/// of the plugins preceding it in <paramref name="previousLines"/> that the merge did not list (first when there is
	/// none), so the plugins that preceded it before the merge still do; the ones the merge added are removed, and the
	/// ones removed since stay removed. The merge moves only the plugins it lists, so with nothing changed since, the
	/// result is the previous list. The current comments stay at the top and blank lines are dropped.
	/// </summary>
	public static IReadOnlyList<string> RevertMerge(IReadOnlyList<string> currentLines, IReadOnlyList<string> mergedLines,
		IReadOnlyList<string> previousLines)
	{
		var mergedNames = ParsePlugins(mergedLines).Select(plugin => plugin.Name).ToHashSet(NameComparer);
		var current = ParsePlugins(currentLines).DistinctBy(plugin => plugin.Name, NameComparer).ToList();
		var currentNames = current.Select(plugin => plugin.Name).ToHashSet(NameComparer);
		var others = current.Where(plugin => !mergedNames.Contains(plugin.Name)).ToList();
		var otherIndices = new Dictionary<string, int>(others.Count, NameComparer);
		for (var i = 0; i < others.Count; i++)
		{
			otherIndices.Add(others[i].Name, i);
		}

		// The merged plugins to restore, each with the number of other plugins that go before it. The numbers never
		// decrease, so the merged plugins keep their previous relative order.
		var restored = new List<(int OthersBefore, Plugin Plugin)>();
		var othersBefore = 0;
		foreach (var plugin in ParsePlugins(previousLines).DistinctBy(plugin => plugin.Name, NameComparer))
		{
			if (otherIndices.TryGetValue(plugin.Name, out var index))
			{
				othersBefore = Math.Max(othersBefore, index + 1);
			}
			else if (mergedNames.Contains(plugin.Name) && currentNames.Contains(plugin.Name))
			{
				restored.Add((othersBefore, plugin));
			}
		}

		var result = new List<Plugin>(others.Count + restored.Count);
		var restoredIndex = 0;
		for (var i = 0; i <= others.Count; i++)
		{
			for (; restoredIndex < restored.Count && restored[restoredIndex].OthersBefore == i; restoredIndex++)
			{
				result.Add(restored[restoredIndex].Plugin);
			}

			if (i < others.Count)
			{
				result.Add(others[i]);
			}
		}

		return ToLines(currentLines, result);
	}

	/// <summary>
	/// Builds <c>loadorder.txt</c> for <paramref name="pluginsLines"/>, the profile's new <c>plugins.txt</c>: the
	/// comments and the plugins <paramref name="currentLoadOrderLines"/> lists that neither the new nor
	/// <paramref name="previousPluginsLines"/>, the replaced <c>plugins.txt</c>, lists (the primary plugins), in their
	/// order, then the plugins of the new <c>plugins.txt</c>. A plugin the new list dropped is therefore dropped too.
	/// </summary>
	public static IReadOnlyList<string> BuildLoadOrder(IReadOnlyList<string> currentLoadOrderLines,
		IReadOnlyList<string> previousPluginsLines, IReadOnlyList<string> pluginsLines)
	{
		var plugins = ParsePlugins(pluginsLines).Select(plugin => plugin.Name).ToArray();
		var listedPlugins = plugins.ToHashSet(NameComparer);
		listedPlugins.UnionWith(ParsePlugins(previousPluginsLines).Select(plugin => plugin.Name));
		return currentLoadOrderLines
			.Where(IsComment)
			.Concat(currentLoadOrderLines.Where(line => IsPlugin(line) && !listedPlugins.Contains(line)))
			.Concat(plugins)
			.ToArray();
	}

	private static string[] ToLines(IReadOnlyList<string> currentLines, IEnumerable<Plugin> plugins) =>
		currentLines
			.Where(IsComment)
			.Concat(plugins.Select(plugin => plugin.IsActive ? ActiveMarker + plugin.Name : plugin.Name))
			.ToArray();

	private static List<Plugin> ParsePlugins(IReadOnlyList<string> lines) =>
		lines
			.Where(IsPlugin)
			.Select(line => line[0] == ActiveMarker ? new Plugin(line[1..], true) : new Plugin(line, false))
			.ToList();

	private static bool IsComment(string line) => line.StartsWith(CommentMarker, StringComparison.Ordinal);

	private static bool IsPlugin(string line) => !string.IsNullOrWhiteSpace(line) && !IsComment(line);

	/// <summary>
	/// Marks the longest strictly increasing subsequence of the non-negative <paramref name="values"/>, which are
	/// distinct; negative values are never marked. Of equally long subsequences it prefers larger values, starting
	/// with the largest possible first value.
	/// </summary>
	private static bool[] FindLongestIncreasingSubsequence(int[] values)
	{
		// Scanning from the end, heads[k] is the index of the largest first value of an increasing subsequence of
		// length k + 1, so the values at heads decrease with k.
		var heads = new List<int>(values.Length);
		var next = new int[values.Length];
		for (var i = values.Length - 1; i >= 0; i--)
		{
			if (values[i] < 0)
			{
				continue;
			}

			// The number of heads larger than values[i]: the longest subsequence values[i] can start.
			int low = 0, high = heads.Count;
			while (low < high)
			{
				var middle = (low + high) / 2;
				if (values[heads[middle]] > values[i])
				{
					low = middle + 1;
				}
				else
				{
					high = middle;
				}
			}

			next[i] = low > 0 ? heads[low - 1] : -1;
			if (low == heads.Count)
			{
				heads.Add(i);
			}
			else
			{
				heads[low] = i;
			}
		}

		var result = new bool[values.Length];
		for (var i = heads.Count > 0 ? heads[^1] : -1; i >= 0; i = next[i])
		{
			result[i] = true;
		}

		return result;
	}

	private readonly record struct Plugin(string Name, bool IsActive);
}

/// <param name="Lines">The merged <c>plugins.txt</c>.</param>
/// <param name="AddedCount">Merged plugins that were not listed.</param>
/// <param name="MovedCount">Listed merged plugins that had to move.</param>
/// <param name="ChangedStateCount">Listed merged plugins whose active state changed.</param>
internal readonly record struct PluginsMergeResult(
	IReadOnlyList<string> Lines, int AddedCount, int MovedCount, int ChangedStateCount);
