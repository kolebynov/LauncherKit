using System.Text.Json.Serialization;
using Avalonia.Controls.Notifications;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.SingleInstance;

namespace SampleLauncher.App;

/// <summary>Sent by every later launch to the running launcher: that launch's arguments and working directory.</summary>
public sealed record SecondLaunchMessage(string[] Args, string WorkingDirectory) : ILauncherMessage
{
	public static string Kind => "SampleLauncher.SecondLaunch";
}

[JsonSerializable(typeof(SecondLaunchMessage))]
internal sealed partial class SampleJsonContext : JsonSerializerContext;

/// <summary>Shows a toast titled <see cref="Title"/> with the later launch's working directory and arguments.</summary>
public sealed class SecondLaunchMessageHandler(INotificationService notifications) : ILauncherMessageHandler<SecondLaunchMessage>
{
	/// <summary>Stable title the automation flows look for.</summary>
	public const string Title = "Launched again";

	public void Handle(SecondLaunchMessage message) => notifications.Show(new AppNotification
	{
		Type = NotificationType.Information,
		Title = Title,
		Message = $"Working directory: {message.WorkingDirectory}\nArguments: "
			+ (message.Args.Length == 0 ? "(none)" : string.Join(' ', message.Args)),
	});
}
