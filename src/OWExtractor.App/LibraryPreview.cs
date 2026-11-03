using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OWExtractor.Core;

namespace OWExtractor.App;

public static class LibraryPreview
{
    private static void EnsureLocalPath(string file, string gamePath)
    {
        if (!Path.IsPathFullyQualified(file)) throw new InvalidDataException("请选择本地缓存图片。");
        var full = Path.GetFullPath(file);
        if (!string.IsNullOrWhiteSpace(gamePath))
        {
            var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath));
            if (full.Equals(game, StringComparison.OrdinalIgnoreCase) || full.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("离线预览不读取游戏目录。");
        }
        CatalogService.EnsureExportPath(Path.GetDirectoryName(full)!);
        var info = new FileInfo(full);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > 32 * 1024 * 1024)
            throw new InvalidDataException("预览图片不能是链接，大小不能超过 32 MB。");
    }

    public static void EnsureAllowed(string file, string gamePath)
    {
        EnsureLocalPath(file, gamePath);
        if (Path.GetExtension(file).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg"))
            throw new InvalidDataException("请选择之前生成的 PNG 或 JPEG 预览图。");
    }
    public static void EnsureMetadataAllowed(string file, string gamePath)
    {
        EnsureLocalPath(file, gamePath);
        if (!Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length > 8 * 1024 * 1024)
            throw new InvalidDataException("请选择小于 8 MB 的历史统计 JSON。");
    }

    public static ImageSource? Load(string? file, string gamePath)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        try
        {
            EnsureAllowed(file, gamePath);
            // Decode once from an explicit cache file. No renderer or model/game parser is started.
            using var stream = File.OpenRead(file);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = 1200; image.StreamSource = stream; image.EndInit(); image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        { return null; }
    }
}

public sealed class SkinCard(HeroAsset hero, SkinAsset skin, string gamePath)
{
    public HeroAsset Hero { get; } = hero;
    public SkinAsset Skin { get; } = skin;
    public string Name => Skin.Name;
    public string Description => Hero.Name + " · " + Skin.State;
    public ImageSource? Thumbnail => LibraryPreview.Load(Skin.PreviewFrontPath, gamePath);
}

public sealed record AudioInventoryRow(string Name, string Description, int Count)
{
    public string CountLabel => $"{Count:N0} 个文件";
}

public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PageMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Equals(value, parameter);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? parameter : Binding.DoNothing;
}

public sealed class CachedImageConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => LibraryPreview.Load(values.ElementAtOrDefault(0) as string, values.ElementAtOrDefault(1) as string ?? "");
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed record AudioCollectionCard(int Chinese, int English, int SoundEffects)
{
    public string ChineseLabel => $"中文语音与对话　{Chinese:N0}";
    public string EnglishLabel => $"英文语音与对话　{English:N0}";
    public string SoundEffectsLabel => $"关联音效　{SoundEffects:N0}";
}
