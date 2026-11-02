using System.Globalization;

namespace LauncherKit.Core.Feed;

/// <summary>Feed content keyed by language; resolution uses the language declared by its containing feed.</summary>
public sealed class LocalizedText : Dictionary<string, string>
{
	internal string DefaultLanguage { get; set; } = "ru";

	public string Resolve() => Resolve(CultureInfo.CurrentUICulture, DefaultLanguage);

	public string Resolve(CultureInfo culture, string defaultLanguage)
	{
		for (var candidate = culture; candidate != CultureInfo.InvariantCulture; candidate = candidate.Parent)
		{
			if (TryGetValue(candidate.Name, out var text))
			{
				return text;
			}
		}

		return this[defaultLanguage];
	}
}
