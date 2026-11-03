using System.Collections;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.Localization;

/// <summary>Resolves each culture's consumer resources before the kit, without ResourceManager parent fallback.</summary>
public sealed class ExactResourceStringLocalizer<T> : IStringLocalizer<T>
{
	private static readonly Action<ILogger, string, string, Exception?> LogMissing = LoggerMessage.Define<string, string>(
		LogLevel.Warning, new EventId(1, "MissingLocalizationResource"),
		"Localization resource {ResourceKey} was not found for UI culture {Culture}");

	private readonly ResourceManager _resources;
	private readonly ResourceManager? _overrides;
	private readonly ILogger _logger;

	public ExactResourceStringLocalizer(ResourceManager resources, ResourceManager? overrides, ILogger logger)
	{
		_resources = resources;
		_overrides = overrides;
		_logger = logger;
	}

	public LocalizedString this[string name] => Find(name);

	public LocalizedString this[string name, params object[] arguments]
	{
		get
		{
			var value = Find(name);
			return new LocalizedString(name, string.Format(CultureInfo.CurrentCulture, value.Value, arguments),
				value.ResourceNotFound, value.SearchedLocation);
		}
	}

	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
	{
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var culture in Cultures(CultureInfo.CurrentUICulture, includeParentCultures))
		{
			if (_overrides is not null)
			{
				foreach (var value in ReadStrings(_overrides, culture, names))
					yield return value;
			}

			foreach (var value in ReadStrings(_resources, culture, names))
				yield return value;
		}
	}

	private LocalizedString Find(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		var culture = CultureInfo.CurrentUICulture;
		var candidate = culture;
		while (true)
		{
			var value = _overrides is null ? null : ExactSet(_overrides, candidate)?.GetString(name);
			if (value is not null)
				return new LocalizedString(name, value, false, _overrides!.BaseName);

			value = ExactSet(_resources, candidate)?.GetString(name);
			if (value is not null)
				return new LocalizedString(name, value, false, _resources.BaseName);

			if (candidate.Equals(CultureInfo.InvariantCulture))
				break;
			candidate = candidate.Parent;
		}

		LogMissing(_logger, name, culture.Name, null);
		return new LocalizedString(name, name, true, _resources.BaseName);
	}

	private static IEnumerable<CultureInfo> Cultures(CultureInfo culture, bool includeParents)
	{
		while (true)
		{
			yield return culture;
			if (!includeParents || culture.Equals(CultureInfo.InvariantCulture))
				yield break;
			culture = culture.Parent;
		}
	}

	private static ResourceSet? ExactSet(ResourceManager manager, CultureInfo culture)
	{
		try
		{
			// Never call GetString on the manager: it caches parent sets under child cultures.
			return manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
		}
		catch (MissingManifestResourceException)
		{
			// Consumer override bundles are optional, including their neutral bundle.
			return null;
		}
	}

	private static IEnumerable<LocalizedString> ReadStrings(ResourceManager manager, CultureInfo culture, HashSet<string> names)
	{
		var set = ExactSet(manager, culture);
		if (set is null)
			yield break;

		foreach (DictionaryEntry entry in set)
		{
			if (entry.Key is string name && entry.Value is string value && names.Add(name))
				yield return new LocalizedString(name, value, false, manager.BaseName);
		}
	}
}
