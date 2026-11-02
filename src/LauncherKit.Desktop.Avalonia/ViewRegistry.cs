using Avalonia.Controls;

namespace LauncherKit.Desktop.Avalonia;

public interface IViewRegistry
{
	void Register<TViewModel, TView>() where TView : Control, new();
	void Register<TViewModel, TView>(Func<IServiceProvider, TView> factory) where TView : Control;
	Control CreateView(Type viewModelType, IServiceProvider services);
}

public sealed class ViewRegistry : IViewRegistry
{
	private readonly Dictionary<Type, Func<IServiceProvider, Control>> _factories = new();

	public void Register<TViewModel, TView>() where TView : Control, new() =>
		Register<TViewModel, TView>(static _ => new TView());

	public void Register<TViewModel, TView>(Func<IServiceProvider, TView> factory) where TView : Control
	{
		ArgumentNullException.ThrowIfNull(factory);
		_factories.Add(typeof(TViewModel), services => factory(services));
	}

	public Control CreateView(Type viewModelType, IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(viewModelType);
		ArgumentNullException.ThrowIfNull(services);
		return _factories.TryGetValue(viewModelType, out var factory)
			? factory(services)
			: throw new InvalidOperationException($"No view is registered for view model '{viewModelType.FullName}'.");
	}
}
