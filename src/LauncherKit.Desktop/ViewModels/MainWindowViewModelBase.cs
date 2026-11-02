namespace LauncherKit.Desktop.ViewModels;

/// <summary>
/// The consumer-owned main window model. The kit only resolves it from the current application scope and assigns
/// it as the window's data context; all main window state and behavior belong to the consumer.
/// </summary>
public abstract class MainWindowViewModelBase : ViewModelBase;
