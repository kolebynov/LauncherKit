using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace LauncherKit.Desktop.Avalonia;

/// <summary>
/// The page factory of every <see cref="FAFrame"/>: pages are navigated to with their view model and built by the
/// <see cref="ViewLocator"/>.
/// </summary>
public sealed class FramePageFactory(ViewLocator viewLocator) : IFANavigationPageFactory
{
	public Control GetPage(Type srcType) =>
		throw new NotSupportedException("Pages must be navigated to with their view model, not a page type.");

	public Control GetPageFromObject(object target)
	{
		ArgumentNullException.ThrowIfNull(target);
		var control = viewLocator.Build(target)!;
		control.DataContext = target;
		return control;
	}
}
