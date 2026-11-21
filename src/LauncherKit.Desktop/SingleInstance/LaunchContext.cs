namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// The inputs of one launcher invocation: its raw command-line arguments, the working directory of the process that was
/// started (resolve relative paths in <see cref="Args"/> against it: for a relaunch it differs from the running launcher's)
/// and whether this invocation started the running launcher (<see langword="true"/>) or was forwarded by a later launch.
/// </summary>
public sealed record LaunchContext(IReadOnlyList<string> Args, string WorkingDirectory, bool IsFirstLaunch);
