using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace LauncherKit.DevAutomation;

/// <summary>Renders roots in-process, so screenshots need neither the window in front nor screen capture rights.</summary>
internal static class Screenshot
{
	/// <summary>
	/// Renders the main window with the open popups drawn over it at their positions, or only the
	/// <paramref name="crop"/> part of it, as PNG. The image grows to the right and down to fit popups that extend past
	/// the window, so its pixels stay at window-local coordinates.
	/// </summary>
	/// <param name="crop">Main-window-local area in device-independent pixels.</param>
	/// <param name="scale">Pixels per device-independent pixel.</param>
	public static byte[] Capture(TopLevel window, IReadOnlyList<TopLevel> popups, Rect? crop, double scale)
	{
		var placedPopups = popups
			.Select(x => (Root: x, Bounds: new Rect(window.PointToClient(x.PointToScreen(default)), x.ClientSize)))
			.ToList();
		var canvas = placedPopups.Aggregate(new Rect(window.ClientSize), (union, x) => union.Union(x.Bounds))
			.Intersect(new Rect(0, 0, double.PositiveInfinity, double.PositiveInfinity));
		using var frame = new RenderTargetBitmap(ToPixels(canvas.Size, scale), new Vector(96 * scale, 96 * scale));
		using (var context = frame.CreateDrawingContext(true))
		{
			foreach (var (root, bounds) in placedPopups.Prepend((window, new Rect(window.ClientSize))))
			{
				using var rootFrame = Render(root, root.ClientSize, scale);
				context.DrawImage(rootFrame, bounds);
			}
		}

		if (crop is not { } area)
		{
			return Encode(frame);
		}

		area = area.Intersect(canvas);
		using var cropped = new RenderTargetBitmap(ToPixels(area.Size, scale), new Vector(96 * scale, 96 * scale));
		using (var context = cropped.CreateDrawingContext(true))
		{
			context.DrawImage(frame, area, new Rect(area.Size));
		}

		return Encode(cropped);
	}

	private static RenderTargetBitmap Render(TopLevel root, Size size, double scale)
	{
		var bitmap = new RenderTargetBitmap(ToPixels(size, scale), new Vector(96 * scale, 96 * scale));
		using (var context = bitmap.CreateDrawingContext(true))
		{
			// Windows with a transparent background (Mica) would otherwise come out transparent.
			context.FillRectangle(GetBackground(root), new Rect(size));
		}

		bitmap.Render(root);
		return bitmap;
	}

	private static SolidColorBrush GetBackground(TopLevel root)
	{
		var isDark = root.ActualThemeVariant == ThemeVariant.Dark;
		return root.TryFindResource("SolidBackgroundFillColorBase", root.ActualThemeVariant, out var resource)
			&& resource is Color color
				? new SolidColorBrush(color)
				: new SolidColorBrush(isDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
	}

	private static PixelSize ToPixels(Size size, double scale) =>
		new(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));

	private static byte[] Encode(RenderTargetBitmap bitmap)
	{
		using var stream = new MemoryStream();
		bitmap.Save(stream, PngBitmapEncoderOptions.Default);
		return stream.ToArray();
	}
}
