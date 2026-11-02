using NetEscapades.EnumGenerators;

namespace LauncherKit.Core.UserSettings;

[EnumExtensions]
public enum FileStoragePeriod
{
	Day,
	Week,
	Month,
	Unlimited,
}