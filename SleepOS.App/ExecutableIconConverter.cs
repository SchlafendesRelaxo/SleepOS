using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Binding = System.Windows.Data.Binding;
using Icon = System.Drawing.Icon;
using SystemIcon = System.Drawing.SystemIcons;

namespace SleepOS.App;

public sealed class ExecutableIconConverter : IValueConverter
{
    private const int MaximumCachedIcons = 256;
    private static readonly ConcurrentDictionary<string, BitmapSource> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BitmapSource FallbackIcon = CreateBitmap(SystemIcon.Application);

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return FallbackIcon;
        }

        return Icons.Count < MaximumCachedIcons ? Icons.GetOrAdd(path, LoadIcon) : LoadIcon(path);
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        Binding.DoNothing;

    private static BitmapSource LoadIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon is null ? FallbackIcon : CreateBitmap(icon);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or Win32Exception or ExternalException)
        {
            return FallbackIcon;
        }
    }

    private static BitmapSource CreateBitmap(Icon icon)
    {
        var bitmap = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            System.Windows.Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(32, 32));
        bitmap.Freeze();
        return bitmap;
    }
}
