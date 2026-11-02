using System.Globalization;
using LauncherKit.Desktop.Avalonia.Extensions;
using LauncherKit.Desktop.Avalonia.Localization;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Localization;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.Avalonia.Tests.Localization;

[TestFixture]
[NonParallelizable]
public sealed class ExactResourceStringLocalizerTests
{
	private CultureInfo _originalUiCulture = null!;
	private CultureInfo _originalCulture = null!;
	private RecordingLogger _logger = null!;
	private ExactResourceStringLocalizer<DesktopStrings> _strings = null!;

	[SetUp]
	public void SetUp()
	{
		_originalUiCulture = CultureInfo.CurrentUICulture;
		_originalCulture = CultureInfo.CurrentCulture;
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
		_logger = new RecordingLogger();
		_strings = CreateLocalizer();
	}

	[TearDown]
	public void TearDown()
	{
		CultureInfo.CurrentUICulture = _originalUiCulture;
		CultureInfo.CurrentCulture = _originalCulture;
	}

	[TestCase("Same", "consumer-child")]
	[TestCase("KitChildVsConsumerParent", "kit-child")]
	[TestCase("KitChildVsConsumerNeutral", "kit-child")]
	[TestCase("KitParentVsConsumerNeutral", "kit-parent")]
	[TestCase("ParentVsNeutral", "consumer-parent")]
	[TestCase("ConsumerNeutral", "consumer-neutral")]
	[TestCase("NeutralOnly", "kit-neutral-only")]
	[TestCase("Empty", "")]
	public void Lookup_UsesConsumerThenKitAtEachCulture(string key, string expected)
	{
		var result = _strings[key];
		Assert.That(result.Value, Is.EqualTo(expected));
		Assert.That(result.ResourceNotFound, Is.False);
		Assert.That(_logger.Entries, Is.Empty);
	}

	[Test]
	public void Lookup_ParentLookupDoesNotPoisonLaterChildLookup()
	{
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
		Assert.That(_strings["Same"].Value, Is.EqualTo("consumer-parent"));
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
		Assert.That(_strings["Same"].Value, Is.EqualTo("consumer-child"));
		Assert.That(_strings["KitChildVsConsumerParent"].Value, Is.EqualTo("kit-child"));
	}

	[Test]
	public void Lookup_NeutralResourcesAreLastForUnrelatedCulture()
	{
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
		Assert.That(_strings["Same"].Value, Is.EqualTo("consumer-neutral"));
		Assert.That(_strings["NeutralOnly"].Value, Is.EqualTo("kit-neutral-only"));
	}

	[Test]
	public void Lookup_InvariantCultureChecksConsumerBeforeKit()
	{
		CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
		Assert.That(_strings["Same"].Value, Is.EqualTo("consumer-neutral"));
	}

	[Test]
	public void Lookup_FormatsWithCurrentCultureRatherThanUiCulture()
	{
		Assert.That(_strings["Format", 1234.5].Value, Is.EqualTo("Hello 1,234.5"));
	}

	[Test]
	public void Lookup_MissingKeyReturnsKeyAndLogsWarning()
	{
		var result = _strings["NoSuchKey"];
		Assert.That(result.Value, Is.EqualTo("NoSuchKey"));
		Assert.That(result.ResourceNotFound, Is.True);
		Assert.That(_logger.Entries, Has.Count.EqualTo(1));
		Assert.That(_logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
		Assert.That(_logger.Entries[0].Message, Does.Contain("NoSuchKey").And.Contain("ru-RU"));
	}

	[Test]
	public void Lookup_AbsentOptionalOverrideBundleStillUsesKit()
	{
		var strings = CreateLocalizer("LauncherKit.Desktop.Avalonia.Tests.Localization.Absent");
		Assert.That(strings["Same"].Value, Is.EqualTo("kit-child"));
		Assert.That(strings["NeutralOnly"].Value, Is.EqualTo("kit-neutral-only"));
	}

	[Test]
	public void Lookup_NoOverrideAssemblyUsesKit()
	{
		var strings = new ExactResourceStringLocalizer<DesktopStrings>(KitResources(), null, _logger);
		Assert.That(strings["Same"].Value, Is.EqualTo("kit-child"));
	}

	[Test]
	public void GetAllStrings_WithParentsMatchesIndividualLookupAndDeduplicates()
	{
		var strings = _strings.GetAllStrings(true).ToArray();
		Assert.That(strings.Select(value => value.Name), Is.Unique);
		Assert.That(strings.Select(value => value.Name), Does.Contain("NeutralOnly")
			.And.Contain("OverrideNeutralOnly").And.Contain("OverrideParentOnly").And.Contain("ChildOnly"));
		foreach (var value in strings)
		{
			Assert.That(value.Value, Is.EqualTo(_strings[value.Name].Value), value.Name);
			Assert.That(value.ResourceNotFound, Is.False);
		}
	}

	[Test]
	public void GetAllStrings_WithoutParentsIncludesOnlyExactCulture()
	{
		var strings = _strings.GetAllStrings(false).ToDictionary(value => value.Name, value => value.Value);
		Assert.That(strings.Keys, Is.EquivalentTo(new[]
		{
			"Same", "OverrideChildOnly", "KitChildVsConsumerParent", "KitChildVsConsumerNeutral", "ChildOnly",
		}));
		Assert.That(strings["Same"], Is.EqualTo("consumer-child"));
	}

	[Test]
	public void GetAllStrings_WithoutParentsDoesNotFallBackWhenCultureHasNoBundle()
	{
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
		Assert.That(_strings.GetAllStrings(false), Is.Empty);
	}

	[Test]
	public void GetAllStrings_InvariantCultureIncludesNeutralBundles()
	{
		CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
		var strings = _strings.GetAllStrings(false).ToDictionary(value => value.Name, value => value.Value);
		Assert.That(strings["Same"], Is.EqualTo("consumer-neutral"));
		Assert.That(strings.Keys, Does.Contain("NeutralOnly").And.Contain("OverrideNeutralOnly"));
	}

	[Test]
	public void Registration_UsesConsumerAssemblyOverrideResourceConvention()
	{
		using var services = new ServiceCollection()
			.AddLauncherKitLocalization(typeof(ExactResourceStringLocalizerTests).Assembly)
			.BuildServiceProvider();
		var strings = services.GetRequiredService<IStringLocalizer<DesktopStrings>>();
		Assert.That(strings["Same"].Value, Is.EqualTo("consumer-child"));
		Assert.That(services.GetRequiredService<TaskTexts>(), Is.Not.Null);
		Assert.That(services.GetRequiredService<ChoiceTexts>(), Is.Not.Null);
		Assert.That(services.GetRequiredService<ExceptionTexts>(), Is.Not.Null);
		Assert.That(services.GetRequiredService<EnumTexts>(), Is.Not.Null);
	}

	[Test]
	public void MarkupExtensions_UseResourceBackedLocalizersWithoutViewModels()
	{
		var desktop = new DesktopStringExtension { Key = "MissingMarkupKey" };
		Assert.That(desktop.ProvideValue(null!), Is.EqualTo("MissingMarkupKey"));
	}

	[Test]
	public void MarkupExtensions_UseConfiguredLocalizersAndCurrentUiCulture()
	{
		var previousDesktop = LocalizationMarkup.DesktopStrings;
		try
		{
			using var services = new ServiceCollection().AddLauncherKitLocalization().BuildServiceProvider();
			LocalizationMarkup.Configure(_strings);
			Assert.That(LocalizationMarkup.TaskTexts, Is.Not.Null);
			var taskTexts = LocalizationMarkup.TaskTexts;
			var choiceTexts = LocalizationMarkup.ChoiceTexts;
			var exceptionTexts = LocalizationMarkup.ExceptionTexts;
			var enumTexts = LocalizationMarkup.EnumTexts;
			var desktop = new DesktopStringExtension("Same");
			Assert.That(desktop.ProvideValue(null!), Is.EqualTo("consumer-child"));
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
			Assert.That(desktop.ProvideValue(null!), Is.EqualTo("consumer-neutral"));
			Assert.That(LocalizationMarkup.TaskTexts, Is.SameAs(taskTexts));
			Assert.That(LocalizationMarkup.ChoiceTexts, Is.SameAs(choiceTexts));
			Assert.That(LocalizationMarkup.ExceptionTexts, Is.SameAs(exceptionTexts));
			Assert.That(LocalizationMarkup.EnumTexts, Is.SameAs(enumTexts));
		}
		finally
		{
			LocalizationMarkup.Configure(previousDesktop);
		}
	}

	[Test]
	public void Registration_WithoutOverridesResolvesKitAndConsumerResources()
	{
		using var services = new ServiceCollection().AddLauncherKitLocalization().BuildServiceProvider();
		var desktop = services.GetRequiredService<IStringLocalizer<DesktopStrings>>();
		Assert.That(desktop[DesktopStrings.StorageDay].ResourceNotFound, Is.False);
	}

	[Test]
	public void Lookup_FormattedMissingKeyRetainsMissingFlagAndLogs()
	{
		var result = _strings["Missing {0}", "value"];
		Assert.That(result.Value, Is.EqualTo("Missing value"));
		Assert.That(result.ResourceNotFound, Is.True);
		Assert.That(_logger.Entries, Has.Count.EqualTo(1));
		Assert.That(_logger.Entries[0].Message, Does.Contain("Missing {0}"));
	}

	private ExactResourceStringLocalizer<DesktopStrings> CreateLocalizer(
		string overridesBaseName = "LauncherKit.Desktop.Avalonia.Tests.Localization.LauncherKit.DesktopStrings") =>
		new(KitResources(), new ResourceManager(overridesBaseName, GetType().Assembly), _logger);

	private static ResourceManager KitResources() =>
		new("LauncherKit.Desktop.Avalonia.Tests.Localization.Kit", typeof(ExactResourceStringLocalizerTests).Assembly);

	private sealed class RecordingLogger : ILogger
	{
		public List<(LogLevel Level, string Message)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
	}
}
