using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using R3;

namespace LauncherKit.Core.Extensions;

public delegate ref T RefProvider<T>();

public static class OptionsExtensions
{
	public static IDisposable UpdateFieldOnChange<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
		this IOptionsMonitor<T> optionsMonitor, RefProvider<T?> fieldProvider)
		where T : class
	{
		fieldProvider() = optionsMonitor.CurrentValue;
		return optionsMonitor.OnChange(newSettings => fieldProvider() = newSettings)!;
	}

	public static Observable<T> ToObservable<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
		this IOptionsMonitor<T> optionsMonitor) =>
		Observable.Create<T, IOptionsMonitor<T>>(
			optionsMonitor,
			static (observer, optionsMonitor) =>
			{
				observer.OnNext(optionsMonitor.CurrentValue);
				return optionsMonitor.OnChange(observer.OnNext) ?? Disposable.Empty;
			});
}