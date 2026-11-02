using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LauncherKit.Infrastructure.Tests;

[TestFixture]
public sealed class SimpleConsoleLoggerTests
{
	[Test]
	public void Log_EnabledLevel_WritesLevelTimestampAndMessage()
	{
		// Arrange
		var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 34, 56, 789, TimeSpan.Zero));
		using var writer = new StringWriter();
		var sut = CreateSut(time, LogLevel.Information, writer);

		// Act
		sut.Log(LogLevel.Warning, new EventId(0), "disk low", null, static (state, _) => state);

		// Assert
		Assert.That(writer.ToString(), Does.Contain("[Warning] 2026.09.08 12:34:56.789 disk low"));
	}

	[Test]
	public void Log_BelowMinimumLevel_WritesNothing()
	{
		// Arrange
		var time = new FakeTimeProvider();
		using var writer = new StringWriter();
		var sut = CreateSut(time, LogLevel.Warning, writer);

		// Act
		sut.Log(LogLevel.Information, new EventId(0), "ignored", null, static (state, _) => state);

		// Assert
		Assert.That(writer.ToString(), Is.Empty);
	}

	[TestCase(LogLevel.Trace, false)]
	[TestCase(LogLevel.Information, false)]
	[TestCase(LogLevel.Warning, true)]
	[TestCase(LogLevel.Error, true)]
	[TestCase(LogLevel.None, false)]
	public void IsEnabled_ConfiguredMinimum_FiltersLevels(LogLevel level, bool expected)
	{
		// Arrange
		using var writer = new StringWriter();
		var sut = CreateSut(new FakeTimeProvider(), LogLevel.Warning, writer);

		// Act
		var result = sut.IsEnabled(level);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[Test]
	[NonParallelizable]
	public void Log_NoExplicitWriter_UsesConsoleOutRedirectedAfterConstruction()
	{
		// Arrange
		var sut = new SimpleConsoleLogger(new FakeTimeProvider(), LogLevel.Information);
		var originalOut = Console.Out;
		using var redirected = new StringWriter();

		// Act
		Console.SetOut(redirected);
		try
		{
			sut.Log(LogLevel.Warning, new EventId(0), "after redirect", null, static (state, _) => state);
		}
		finally
		{
			Console.SetOut(originalOut);
		}

		// Assert
		Assert.That(redirected.ToString(), Does.Contain("after redirect"));
	}

	private static SimpleConsoleLogger CreateSut(TimeProvider timeProvider, LogLevel minimumLevel, TextWriter writer)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(timeProvider);
		mocker.Use(minimumLevel);
		mocker.Use(writer);
		return mocker.CreateInstance<SimpleConsoleLogger>();
	}
}
