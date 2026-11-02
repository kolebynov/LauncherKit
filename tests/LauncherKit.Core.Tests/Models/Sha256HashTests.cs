using LauncherKit.Core.Models;

namespace LauncherKit.Core.Tests.Models;

[TestFixture]
public sealed class Sha256HashTests
{
	private const string ValidHex = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

	[Test]
	public void ToString_ConstructedFromBytes_ReturnsUppercaseHex()
	{
		// Arrange
		var bytes = Convert.FromHexString(ValidHex);

		// Act
		var hash = new Sha256Hash(bytes);

		// Assert
		Assert.That(hash.ToString(), Is.EqualTo(ValidHex));
	}

	[TestCase(31)]
	[TestCase(33)]
	[TestCase(0)]
	public void Constructor_HashLengthIsNot32Bytes_ThrowsArgumentException(int length)
	{
		// Arrange
		var bytes = new byte[length];

		// Act
		// Assert
		Assert.That(() => new Sha256Hash(bytes), Throws.ArgumentException);
	}

	[Test]
	public void Constructor_LowercaseHexString_ParsesToSameValueAsUppercase()
	{
		// Arrange
		var lowercase = ValidHex.ToLowerInvariant();

		// Act
		var hash = new Sha256Hash(lowercase);

		// Assert
		Assert.That(hash, Is.EqualTo(new Sha256Hash(ValidHex)));
	}

	[Test]
	public void Constructor_NonHexString_ThrowsFormatException()
	{
		// Act
		// Assert
		Assert.That(() => new Sha256Hash(new string('z', 64)), Throws.InstanceOf<FormatException>());
	}

	[Test]
	public void Equals_SameContentDifferentInstances_ReturnsTrue()
	{
		// Arrange
		var left = new Sha256Hash(Convert.FromHexString(ValidHex));
		var right = new Sha256Hash(Convert.FromHexString(ValidHex));

		// Act
		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(left.Equals(right), Is.True);
			Assert.That(left == right, Is.True);
			Assert.That(left != right, Is.False);
			Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
		});
	}

	[Test]
	public void Equals_DifferentContent_ReturnsFalse()
	{
		// Arrange
		var left = new Sha256Hash(ValidHex);
		var rightBytes = Convert.FromHexString(ValidHex);
		rightBytes[31] ^= 0xFF;
		var right = new Sha256Hash(rightBytes);

		// Act
		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(left.Equals(right), Is.False);
			Assert.That(left == right, Is.False);
			Assert.That(left != right, Is.True);
		});
	}

	[Test]
	public void Equals_ObjectOfAnotherType_ReturnsFalse()
	{
		// Arrange
		var hash = new Sha256Hash(ValidHex);

		// Act
		var result = hash.Equals(ValidHex);

		// Assert
		Assert.That(result, Is.False);
	}
}
