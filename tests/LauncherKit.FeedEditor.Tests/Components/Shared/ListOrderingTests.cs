using LauncherKit.FeedEditor.Components.Shared;

namespace LauncherKit.FeedEditor.Tests.Components.Shared;

[TestFixture]
public sealed class ListOrderingTests
{
	private static bool IsA(string item) => item[0] == 'A';

	[TestCase(2, -1, 0)]
	[TestCase(0, 1, 2)]
	[TestCase(0, -1, -1)]
	[TestCase(2, 1, -1)]
	public void NeighborIndex_InterleavedGroups_SkipsOtherGroups(int index, int direction, int expected)
	{
		// Arrange
		string[] items = ["A1", "B1", "A2", "B2"];

		// Act
		var result = ListOrdering.NeighborIndex(items, index, direction, IsA);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[Test]
	public void InsertAfterLastIndex_MatchingEntries_ReturnsIndexAfterLastMatch()
	{
		// Arrange
		string[] items = ["A1", "B1", "A2", "B2"];

		// Act
		var result = ListOrdering.InsertAfterLastIndex(items, IsA);

		// Assert
		Assert.That(result, Is.EqualTo(3));
	}

	[Test]
	public void InsertAfterLastIndex_NoMatchingEntries_ReturnsListEnd()
	{
		// Arrange
		string[] items = ["A1", "B1", "A2", "B2"];

		// Act
		var result = ListOrdering.InsertAfterLastIndex(items, item => item[0] == 'C');

		// Assert
		Assert.That(result, Is.EqualTo(items.Length));
	}
}
