using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Loader;
using LauncherKit.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LauncherKit.ProductIdentity.Generator.Tests;

[TestFixture]
public sealed class ProductIdentityGeneratorTests
{
	[Test]
	public void Generate_CompleteProperties_CreatesOptionsWithExactValues()
	{
		// Arrange
		var properties = CompleteProperties();
		properties["LauncherDisplayName"] = "Quoted \"Example\" Launcher";
		properties["LauncherModOrganizerDirectoryName"] = "[EX] Mod Organizer";
		properties["LauncherUserModsSeparator"] = "ВАШИ МОДЫ";
		properties["LauncherModpackVersionFile"] = "[EX] Mod Organizer/mods/version.txt";

		// Act
		var (compilation, diagnostics) = Run(properties);
		var options = CreateOptions(compilation, "Example.Product");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(diagnostics, Is.Empty);
			Assert.That(options.DisplayName, Is.EqualTo("Quoted \"Example\" Launcher"));
			Assert.That(options.ModOrganizerDirectoryName, Is.EqualTo("[EX] Mod Organizer"));
			Assert.That(options.UserModsSeparator, Is.EqualTo("ВАШИ МОДЫ"));
			Assert.That(options.ModpackVersionFile, Is.EqualTo(Path.Combine("[EX] Mod Organizer", "mods", "version.txt")));
			Assert.That(options.RegistryKey, Is.EqualTo(@"HKEY_CURRENT_USER\Software\Example\Launcher"));
			Assert.DoesNotThrow(options.Validate);
		});
	}

	[TestCase("LauncherProductNamespace")]
	[TestCase("LauncherRegistryKey")]
	[TestCase("LauncherOAuthTokenEntropy")]
	public void Generate_MissingOrBlankProperty_ReportsErrorWithoutSource(string property)
	{
		foreach (var value in new string?[] { null, "", " \t" })
		{
			// Arrange
			var properties = CompleteProperties();
			properties[property] = value;

			// Act
			var (compilation, diagnostics) = Run(properties);

			// Assert
			Assert.Multiple(() =>
			{
				Assert.That(diagnostics.Select(x => (x.Id, x.Severity, x.GetMessage(CultureInfo.InvariantCulture))), Is.EqualTo(new[]
				{
					("LKPI001", DiagnosticSeverity.Error, $"MSBuild property '{property}' is required to generate the product identity"),
				}), value);
				Assert.That(compilation.SyntaxTrees.Count(), Is.EqualTo(1), value);
			});
		}
	}

	private static Dictionary<string, string?> CompleteProperties()
	{
		var properties = ProductIdentityGenerator.OptionNames.ToDictionary(name => "Launcher" + name, name => (string?)$"{name}-value");
		properties["LauncherProductNamespace"] = "Example.Product";
		properties["LauncherRegistryKey"] = @"HKEY_CURRENT_USER\Software\Example\Launcher";
		return properties;
	}

	private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Run(Dictionary<string, string?> properties)
	{
		var compilation = CSharpCompilation.Create(
			"ProductIdentityGeneratorTest",
			[CSharpSyntaxTree.ParseText("namespace Example.Product { internal static class Anchor { } }")],
			((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
				.Append(typeof(LauncherProductOptions).Assembly.Location)
				.Select(path => MetadataReference.CreateFromFile(path)),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		var driver = CSharpGeneratorDriver.Create(
			[new ProductIdentityGenerator().AsSourceGenerator()],
			optionsProvider: new GlobalOptionsProvider(properties
				.Where(x => x.Value != null)
				.ToDictionary(x => "build_property." + x.Key, x => x.Value!)));
		driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
		return (output, diagnostics);
	}

	private static LauncherProductOptions CreateOptions(Compilation compilation, string @namespace)
	{
		using var stream = new MemoryStream();
		var result = compilation.Emit(stream);
		Assert.That(result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error), Is.Empty);
		stream.Position = 0;
		var context = new AssemblyLoadContext(null, isCollectible: true);
		try
		{
			var type = context.LoadFromStream(stream).GetType($"{@namespace}.ProductIdentity", throwOnError: true)!;
			return (LauncherProductOptions)type.GetMethod("CreateOptions")!.Invoke(null, null)!;
		}
		finally
		{
			context.Unload();
		}
	}

	private sealed class GlobalOptionsProvider(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
	{
		public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(values);

		public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options([]);

		public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options([]);

		private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
		{
			public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
		}
	}
}
