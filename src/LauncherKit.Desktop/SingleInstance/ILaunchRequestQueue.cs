using System.Diagnostics.CodeAnalysis;
using R3;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>The consuming side of the <see cref="LaunchRequestQueue"/>, drained by the <see cref="LaunchRequestPump"/>.</summary>
internal interface ILaunchRequestQueue
{
	/// <summary>Raised after each admitted request, on the admitting thread.</summary>
	Observable<Unit> Enqueued { get; }

	bool TryDequeue([MaybeNullWhen(false)] out LaunchContext request);
}
