using System.Text.Json;
using LauncherKit.Infrastructure;
using NuGet.Versioning;
using LauncherKit.Core.JsonConverters;

namespace LauncherKit.Core.Tests.JsonConverters;

[TestFixture]
public sealed class VersionRangeJsonConverterTests
{
	[Test]
	public void Read_InclusiveExclusiveRange_ReturnsRangeWithSameBounds()
	{
		// Arrange
		var options = CreateOptions();

		// Act
		var range = JsonSerializer.Deserialize<VersionRange>("\"[1.0.0,2.0.0)\"", options);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(range!.Satisfies(NuGetVersion.Parse("1.0.0")), Is.True);
			Assert.That(range.Satisfies(NuGetVersion.Parse("1.9.9")), Is.True);
			Assert.That(range.Satisfies(NuGetVersion.Parse("2.0.0")), Is.False);
		});
	}

	[Test]
	public void Read_MinimumOnlyRange_ReturnsFloatingUpperBound()
	{
		// Arrange
		var options = CreateOptions();

		// Act
		var range = JsonSerializer.Deserialize<VersionRange>("\"1.5.0\"", options);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(range!.Satisfies(NuGetVersion.Parse("1.4.0")), Is.False);
			Assert.That(range.Satisfies(NuGetVersion.Parse("9.0.0")), Is.True);
		});
	}

	[Test]
	public void Write_Range_WritesParsableRangeString()
	{
		// Arrange
		var options = CreateOptions();
		var range = VersionRange.Parse("[1.0.0,2.0.0)");

		// Act
		var json = JsonSerializer.Serialize(range, options);

		// Assert
		Assert.That(JsonSerializer.Deserialize<VersionRange>(json, options), Is.EqualTo(range));
	}

	[Test]
	public void Read_StringThatIsNotARange_ThrowsJsonException()
	{
		// Arrange
		var options = CreateOptions();

		// Act
		// Assert
		Assert.That(() => JsonSerializer.Deserialize<VersionRange>("\"(((\"", options), Throws.InstanceOf<JsonException>());
	}

	private static JsonSerializerOptions CreateOptions() => new() { Converters = { VersionRangeJsonConverter.Instance } };
}
