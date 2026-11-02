using System.ComponentModel;
using System.Runtime.CompilerServices;
using R3;

namespace LauncherKit.Core.Extensions;

public static class ObservableExtensions
{
	public static IDisposable SubscribeSafe<T>(this Observable<T> observable, Action<T> onNext)
	{
		try
		{
			return observable.Subscribe(onNext);
		}
		catch
		{
			return Disposable.Empty;
		}
	}

	public static Observable<(TProp1?, TProp2?)> ObserveMultiplePropertyChanged<T, TProp1, TProp2>(
		this T value, Func<T, TProp1?> propertySelector1, Func<T, TProp2?> propertySelector2,
		bool pushCurrentValueOnSubscribe = true,
		CancellationToken cancellationToken = default,
		[CallerArgumentExpression(nameof(propertySelector1))] string? expr1 = null,
		[CallerArgumentExpression(nameof(propertySelector2))] string? expr2 = null)
		where T : INotifyPropertyChanged =>
		value.ObservePropertyChanged(propertySelector1, pushCurrentValueOnSubscribe, cancellationToken, expr1)
			.CombineLatest(value.ObservePropertyChanged(propertySelector2, pushCurrentValueOnSubscribe, cancellationToken, expr2), (x, y) => (x, y));

	public static Observable<(TProp1?, TProp2?, TProp3?)> ObserveMultiplePropertyChanged<T, TProp1, TProp2, TProp3>(
		this T value, Func<T, TProp1?> propertySelector1, Func<T, TProp2?> propertySelector2, Func<T, TProp3?> propertySelector3,
		bool pushCurrentValueOnSubscribe = true,
		CancellationToken cancellationToken = default,
		[CallerArgumentExpression(nameof(propertySelector1))] string? expr1 = null,
		[CallerArgumentExpression(nameof(propertySelector2))] string? expr2 = null,
		[CallerArgumentExpression(nameof(propertySelector3))] string? expr3 = null)
		where T : INotifyPropertyChanged =>
		value.ObservePropertyChanged(propertySelector1, pushCurrentValueOnSubscribe, cancellationToken, expr1)
			.CombineLatest(
				value.ObservePropertyChanged(propertySelector2, pushCurrentValueOnSubscribe, cancellationToken, expr2),
				value.ObservePropertyChanged(propertySelector3, pushCurrentValueOnSubscribe, cancellationToken, expr3),
				(x, y, z) => (x, y, z));

	public static Observable<string?> ObserveAnyPropertyChanged<T>(this T value, CancellationToken cancellationToken = default)
		where T : INotifyPropertyChanged =>
		Observable.FromEvent<PropertyChangedEventHandler, string?>(
			h => (_, args) => h(args.PropertyName), h => value.PropertyChanged += h, h => value.PropertyChanged -= h,
			cancellationToken);
}