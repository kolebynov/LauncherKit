using System.Text.Json;
using LauncherKit.Core.Feed.Install;

namespace LauncherKit.Core.Tests.Feed.Install;

[TestFixture]
public sealed class CompareOperatorTests
{
	[TestCase(CompareOperator.Equal, "\"Equal\"")]
	[TestCase(CompareOperator.NotEqual, "\"NotEqual\"")]
	public void Serialize_Value_WritesName(CompareOperator compareOperator, string expectedJson)
	{
		// Act
		var json = JsonSerializer.Serialize(compareOperator, SourceGeneratedJsonContext.Default.CompareOperator);

		// Assert
		Assert.That(json, Is.EqualTo(expectedJson));
	}

	[TestCase("\"Equal\"", CompareOperator.Equal)]
	[TestCase("\"NotEqual\"", CompareOperator.NotEqual)]
	public void Deserialize_Name_ReadsValue(string json, CompareOperator expected)
	{
		// Act
		var result = JsonSerializer.Deserialize(json, SourceGeneratedJsonContext.Default.CompareOperator);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[Test]
	public void Deserialize_UnknownName_ThrowsJsonException()
	{
		// Act & Assert
		Assert.That(
			() => JsonSerializer.Deserialize("\"Greater\"", SourceGeneratedJsonContext.Default.CompareOperator),
			Throws.InstanceOf<JsonException>());
	}
}
