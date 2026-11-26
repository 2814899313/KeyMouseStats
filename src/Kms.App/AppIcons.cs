using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Kms.App;

/// <summary>应用图标（exe / 主窗 / 报表窗 / 托盘共用同一份 .ico）。</summary>
internal static class AppIcons
{
    public const string ResourceUri = "avares://Kms.App/Assets/app-icon.ico";

    public static WindowIcon Load() => new(AssetLoader.Open(new Uri(ResourceUri)));
}
