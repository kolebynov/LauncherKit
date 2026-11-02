using FluentAvalonia.UI.Windowing;

namespace LauncherKit.Desktop.Avalonia;

/// <summary>
/// The consumer-owned main window. Register its implementation as a singleton under this type;
/// the application host resolves it at startup and assigns the current main view model as its data context.
/// </summary>
public abstract class MainView : FAAppWindow;
