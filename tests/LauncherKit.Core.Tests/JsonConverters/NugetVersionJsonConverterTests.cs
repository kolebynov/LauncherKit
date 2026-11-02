using System.Text.Json;
using NuGet.Versioning;
using LauncherKit.Core.JsonConverters;

namespace LauncherKit.Core.Tests.JsonConverters;

[TestFixture]
public sealed class NugetVersionJsonConverterTests
{
	[TestCase("1.18.0")]
	[TestCase("2.0.0-beta.3")]
	public void Read_ValidVersionString_ReturnsParsedVersion(string version)
	{
		// Arrange
		var options = CreateOptions();

		// Act
		var result = JsonSerializer.Deserialize<NuGetVersion>($"\"{version}\"", options);

		// Assert
		Assert.That(result, Is.EqualTo(NuGetVersion.Parse(version)));
	}

	[Test]
	public void Write_Version_WritesVersionString()
	{
		// Arrange
		var options = CreateOptions();

		// Act
		var json = JsonSerializer.Serialize(NuGetVersion.Parse("1.18.0"), options);

		// Assert
		Assert.That(json, Is.EqualTo("\"1.18.0\""));
	}

	[Test]
	public void Read_StringThatIsNotAVersion_ThrowsJsonException()
	{
		// Arrange
		var options = CreateOptions();

		// Act
		// Assert
		Assert.That(() => JsonSerializer.Deserialize<NuGetVersion>("\"not-a-version\"", options), Throws.InstanceOf<JsonException>());
	}

	private static JsonSerializerOptions CreateOptions() => new() { Converters = { new NugetVersionJsonConverter() } };
}
