using LauncherKit.Core.Models;

namespace LauncherKit.Core;

/// <summary>
/// A program the launcher starts through Mod Organizer: <paramref name="Name"/> is shown in task texts, and a
/// <c>null</c> <paramref name="ProgramPath"/> opens Mod Organizer itself.
/// </summary>
public sealed record GameLaunch(string Name, SkyrimRelativePath? ProgramPath);
