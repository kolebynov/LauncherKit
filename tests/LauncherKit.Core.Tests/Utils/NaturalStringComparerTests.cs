using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Tests.Utils;

[TestFixture]
public sealed class NaturalStringComparerTests
{
	[Test]
	public void Compare_NumericSuffixes_OrdersNumericallyNotLexicographically()
	{
		// Arrange
		var comparer = NaturalStringComparer.Instance;

		// Act
		var result = comparer.Compare("mod2.esp", "mod10.esp");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.Negative);
			Assert.That(string.CompareOrdinal("mod2.esp", "mod10.esp"), Is.Positive);
		});
	}

	[Test]
	public void Compare_EqualStrings_ReturnsZero()
	{
		// Arrange
		var comparer = NaturalStringComparer.Instance;

		// Act
		var result = comparer.Compare("00 Textures.bsa", "00 Textures.bsa");

		// Assert
		Assert.That(result, Is.Zero);
	}

	[Test]
	public void Compare_NullOperand_TreatsNullAsEmptyString()
	{
		// Arrange
		var comparer = NaturalStringComparer.Instance;

		// Act
		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(comparer.Compare(null, string.Empty), Is.Zero);
			Assert.That(comparer.Compare(null, "a"), Is.Negative);
			Assert.That(comparer.Compare("a", null), Is.Positive);
		});
	}

	[Test]
	public void Compare_UsedAsSortComparer_ProducesNaturalOrder()
	{
		// Arrange
		var items = new List<string> { "part10", "part2", "part1", "part20", "part3" };

		// Act
		items.Sort(NaturalStringComparer.Instance);

		// Assert
		Assert.That(items, Is.EqualTo(new[] { "part1", "part2", "part3", "part10", "part20" }));
	}
}
