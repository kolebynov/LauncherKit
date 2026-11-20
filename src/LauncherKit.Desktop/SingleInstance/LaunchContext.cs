namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// The inputs of one launcher invocation: its command-line arguments and the working directory of the process that was
/// started, which differs from the running launcher's when a relaunch forwards its message.
/// </summary>
public sealed record LaunchContext(IReadOnlyList<string> Args, string WorkingDirectory);
