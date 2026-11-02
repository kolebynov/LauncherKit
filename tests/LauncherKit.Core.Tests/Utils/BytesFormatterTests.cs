using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Tests.Utils;

// Format uses the ambient culture, so the fixture pins it to keep the expected decimal separator stable.
[TestFixture]
[SetCulture("en-US")]
public sealed class BytesFormatterTests
{
	[Test]
	public void Format_SpeedMeasurement_AppendsPerSecondSuffix()
	{
		// Act
		var result = BytesFormatter.Format(1536, BytesFormatter.Measurement.Speed.Instance);

		// Assert
		Assert.That(result, Is.EqualTo("1.50 KB/s"));
	}
}
