using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Kms.App.Theming;

/// <summary>V3 美术皮肤的页面背景映射。资源在 app 内以 avares 提供。</summary>
internal static class ThemeArtwork
{
    internal sealed record SkinInfo(
        string Id,
        string Name,
        string ImagePath,
        string ThumbPath,
        int Dim,
        Avalonia.Media.Color Background,
        Avalonia.Media.Color Accent,
        Avalonia.Media.Color Text);

    private static readonly Dictionary<int, string[]> Pages = new()
    {
        [2] =
        [
            "ori-overview", "ori-trend", "ori-hours", "ori-keys", "ori-apps",
            "ori-insights", "ori-text", "ori-day", "ori-heat"
        ],
        [5] =
        [
            "nikki-dream", "nikki-pose-trend", "nikki-pose-hours", "nikki-pose-keys",
            "nikki-pose-apps", "nikki-pose-insights", "nikki-text", "nikki-day", "nikki-heat"
        ],
        [6] =
        [
            "halo-overview", "halo-trend", "halo-hours", "halo-keys", "halo-apps",
            "halo-insights", "halo-text", "halo-day", "halo-heat"
        ],
        [7] =
        [
            "resident-overview", "resident-trend", "resident-hours", "resident-keys",
            "resident-apps", "resident-insights", "resident-text", "resident-day", "resident-heat"
        ],
        [8] =
        [
            "minecraft-overview", "minecraft-trend", "minecraft-hours", "minecraft-keys",
            "minecraft-apps", "minecraft-insights", "minecraft-text", "minecraft-day", "minecraft-heat"
        ],
        [9] =
        [
            "balatro-backgrounds", "balatro-backgrounds", "balatro-backgrounds",
            "balatro-backgrounds", "balatro-backgrounds", "balatro-backgrounds",
            "balatro-text", "balatro-day", "balatro-heat"
        ],
        [10] =
        [
            "wuxia-backgrounds", "wuxia-backgrounds", "wuxia-backgrounds",
            "wuxia-backgrounds", "wuxia-backgrounds", "wuxia-backgrounds",
            "wuxia-text", "wuxia-day", "wuxia-heat"
        ],
        [11] =
        [
            "eurotruck-overview", "eurotruck-trend", "eurotruck-hours", "eurotruck-keys",
            "eurotruck-apps", "eurotruck-insights", "eurotruck-text", "eurotruck-day",
            "eurotruck-heat"
        ],
        [12] =
        [
            "gujian-overview", "gujian-trend", "gujian-hours", "gujian-keys", "gujian-apps",
            "gujian-insights", "gujian-text", "gujian-day", "gujian-heat"
        ]
    };

    public static bool IsArtTheme(int themeId) => Pages.ContainsKey(themeId);

    /// <summary>当前是否启用自定义壁纸；供卡片/页头计算半透明底板。</summary>
    public static bool CustomActive { get; set; }

    public static Bitmap? Load(int themeId, int page)
    {
        if (!Pages.TryGetValue(themeId, out string[]? names) || names.Length == 0)
        {
            return null;
        }

        string name = names[Math.Clamp(page, 0, names.Length - 1)];
        try
        {
            return new Bitmap(AssetLoader.Open(new Uri($"avares://Kms.App/Assets/Theme/{name}.jpg")));
        }
        catch
        {
            return null;
        }
    }

    public static string PreviewName(int themeId, int page = 0) =>
        Pages.TryGetValue(themeId, out string[]? names) && names.Length > 0
            ? names[Math.Clamp(page, 0, names.Length - 1)]
            : "";

    public static Bitmap? LoadCustom(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读取 v3 自定义皮肤清单；只读 v3 文件，不修改。</summary>
    public static List<SkinInfo> LoadV3Skins()
    {
        List<SkinInfo> skins = [];
        string root = Path.Combine(DataPaths.V3TextRoot, "themes");
        string config = Path.Combine(root, "custom-themes.json");
        if (!File.Exists(config))
        {
            return skins;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(config));
            if (!document.RootElement.TryGetProperty("skins", out JsonElement list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return skins;
            }

            int index = 1;
            foreach (JsonElement item in list.EnumerateArray())
            {
                string id = item.TryGetProperty("id", out JsonElement idElement)
                    ? idElement.GetString() ?? ""
                    : "";
                string name = item.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString() ?? ""
                    : "";
                string image = item.TryGetProperty("image", out JsonElement imageElement)
                    ? imageElement.GetString() ?? ""
                    : "";
                string cache = item.TryGetProperty("cache", out JsonElement cacheElement)
                    ? cacheElement.GetString() ?? ""
                    : "";
                string thumb = item.TryGetProperty("thumb", out JsonElement thumbElement)
                    ? thumbElement.GetString() ?? ""
                    : "";
                int dim = item.TryGetProperty("dim", out JsonElement dimElement)
                    && dimElement.TryGetInt32(out int parsedDim)
                    ? parsedDim
                    : 70;

                string imagePath = "";
                foreach (string candidate in new[] { image, cache })
                {
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        string full = Path.Combine(root, candidate);
                        if (File.Exists(full))
                        {
                            imagePath = full;
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    continue;
                }

                string thumbPath = "";
                if (!string.IsNullOrWhiteSpace(thumb))
                {
                    string full = Path.Combine(root, thumb);
                    if (File.Exists(full))
                    {
                        thumbPath = full;
                    }
                }

                if (string.IsNullOrWhiteSpace(name) || Guid.TryParse(name, out _))
                {
                    name = $"自定义皮肤 {index}";
                }

                Color background = ParseColor(item, "background", Color.FromRgb(15, 20, 30));
                Color accent = ParseColor(item, "accent", Color.FromRgb(110, 168, 255));
                Color text = ParseColor(item, "text", Color.FromRgb(246, 248, 253));
                skins.Add(new SkinInfo(id, name, imagePath, thumbPath, dim, background, accent, text));
                index++;
            }
        }
        catch
        {
        }

        return skins;
    }

    private static Color ParseColor(JsonElement item, string property, Color fallback)
    {
        if (!item.TryGetProperty(property, out JsonElement element)
            || element.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        string? value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        return Color.TryParse(value, out Color parsed) ? parsed : fallback;
    }

    /// <summary>尝试把 v3 的当前自定义皮肤迁移到 v4（只读 v3 文件，不修改）。</summary>
    public static bool TryFindV3Custom(out string imagePath, out string name)
    {
        imagePath = "";
        name = "自定义皮肤";
        string root = Path.Combine(DataPaths.V3TextRoot, "themes");
        string config = Path.Combine(root, "custom-themes.json");
        if (!File.Exists(config))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(config));
            JsonElement rootElement = document.RootElement;
            if (!rootElement.TryGetProperty("active", out JsonElement active)
                || active.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string? activeId = active.GetString();
            if (string.IsNullOrEmpty(activeId))
            {
                return false;
            }

            foreach (SkinInfo skin in LoadV3Skins())
            {
                if (skin.Id == activeId)
                {
                    imagePath = skin.ImagePath;
                    name = skin.Name;
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }
}
