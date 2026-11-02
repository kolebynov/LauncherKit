using System.Text.Json;
using LauncherKit.Core.JsonConverters;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.Tests.JsonConverters;

[TestFixture]
public sealed class Sha256HashJsonConverterTests
{
	private const string ValidHex = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

	[Test]
	public void Read_HexString_ReturnsHash()
	{
		// Arrange
		var options = CreateOptions();

		// Act
		var hash = JsonSerializer.Deserialize<Sha256Hash>($"\"{ValidHex}\"", options);

		// Assert
		Assert.That(hash, Is.EqualTo(new Sha256Hash(ValidHex)));
	}

	[Test]
	public void Write_Hash_WritesUppercaseHexString()
	{
		// Arrange
		var options = CreateOptions();

		// Act
		var json = JsonSerializer.Serialize(new Sha256Hash(ValidHex), options);

		// Assert
		Assert.That(json, Is.EqualTo($"\"{ValidHex}\""));
	}

	[Test]
	public void Read_EmptyString_ThrowsJsonException()
	{
		// Arrange
		var options = CreateOptions();

		// Act
		// Assert
		Assert.That(() => JsonSerializer.Deserialize<Sha256Hash>("\"\"", options), Throws.InstanceOf<JsonException>());
	}

	[Test]
	public void Read_NullToken_ThrowsJsonException()
	{
		// Arrange
		var options = CreateOptions();

		// Act
		// Assert
		Assert.That(() => JsonSerializer.Deserialize<Sha256Hash>("null", options), Throws.InstanceOf<JsonException>());
	}

	[Test]
	public void Read_StringThatIsNotAHash_ThrowsFormatException()
	{
		// Arrange
		var options = CreateOptions();

		// Act
		// Assert
		Assert.That(() => JsonSerializer.Deserialize<Sha256Hash>("\"not-a-hash\"", options), Throws.InstanceOf<FormatException>());
	}

	private static JsonSerializerOptions CreateOptions() => new() { Converters = { new Sha256HashJsonConverter() } };
}
