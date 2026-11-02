namespace LauncherKit.Desktop.Avalonia;

/// <summary>
/// Identifies the consumer's main window registration. The implementation must derive from
/// <see cref="global::Avalonia.Controls.Window"/>; the consumer chooses its window base class.
/// Register it as a singleton. The application host assigns the current main model as its data context.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces",
	Justification = "DI registration contract for the consumer's main Window without constraining its base class or exposing consumer behavior.")]
public interface IMainView;
