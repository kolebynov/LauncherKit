using System.Globalization;
using LauncherKit.Desktop.Avalonia.Localization;
using Avalonia.Data.Converters;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;

namespace LauncherKit.Desktop.Avalonia.Converters;

public sealed class TaskDescriptionConverter : IValueConverter
{
	public static TaskDescriptionConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
	{
		AppTaskDescription description => LocalizationMarkup.TaskTexts.Get(description).Value,
		MicroTaskDescription description => LocalizationMarkup.TaskTexts.Get(description).Value,
		null => null,
		_ => throw new ArgumentException("Expected a task description", nameof(value)),
	};

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
