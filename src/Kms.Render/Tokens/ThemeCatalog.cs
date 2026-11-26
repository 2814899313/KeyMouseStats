using System.Reflection;
using System.Text.Json;
using Avalonia.Media;

namespace Kms.Render.Tokens;

/// <summary>13 套内置主题。唯一真源是嵌入的 <c>Tokens/themes.json</c>。</summary>
public static class ThemeCatalog
{
    private const string ResourceName = "Kms.Render.Tokens.themes.json";

    private static readonly Color[] DefaultHeatStops =
    [
        Color.Parse("#2A50CC"),
        Color.Parse("#14B4DC"),
        Color.Parse("#F5DD43"),
        Color.Parse("#F48328"),
        Color.Parse("#D12F2D")
    ];

    public static IReadOnlyList<ThemeTokens> All { get; } = Load();

    public static ThemeTokens Get(int id) =>
        id >= 0 && id < All.Count ? All[id] : All[0];

    public static int Validate(int id) => id >= 0 && id < All.Count ? id : 0;

    private static IReadOnlyList<ThemeTokens> Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"缺少嵌入资源 {ResourceName}");

        ThemeDto[]? dtos = JsonSerializer.Deserialize<ThemeDto[]>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (dtos is null || dtos.Length != 13)
        {
            throw new InvalidOperationException("themes.json 必须正好包含 13 套主题。");
        }

        ThemeTokens[] result = new ThemeTokens[dtos.Length];
        for (int i = 0; i < dtos.Length; i++)
        {
            ThemeDto dto = dtos[i];
            if (dto.Id != i)
            {
                throw new InvalidOperationException($"themes.json 的 id 必须连续：期待 {i}，实际 {dto.Id}。");
            }

            IReadOnlyList<Color> heat = dto.Heat is { Length: 5 }
                ? dto.Heat.Select(Color.Parse).ToArray()
                : DefaultHeatStops;

            result[i] = new ThemeTokens(
                dto.Id,
                dto.Name ?? "",
                dto.Radius,
                Parse(dto.Background),
                Parse(dto.Card),
                Parse(dto.Raised),
                Parse(dto.Line),
                Parse(dto.Text),
                Parse(dto.Muted),
                Parse(dto.Accent),
                Parse(dto.OnAccent),
                Parse(dto.Green),
                Parse(dto.Orange),
                Parse(dto.Purple),
                Parse(dto.Red),
                Parse(dto.Cyan),
                Parse(dto.Sidebar),
                heat);
        }

        return result;
    }

    private static Color Parse(string? value) =>
        Color.Parse(string.IsNullOrWhiteSpace(value) ? "#000000" : value);

    private sealed class ThemeDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public double Radius { get; set; }
        public string? Background { get; set; }
        public string? Card { get; set; }
        public string? Raised { get; set; }
        public string? Line { get; set; }
        public string? Text { get; set; }
        public string? Muted { get; set; }
        public string? Accent { get; set; }
        public string? OnAccent { get; set; }
        public string? Green { get; set; }
        public string? Orange { get; set; }
        public string? Purple { get; set; }
        public string? Red { get; set; }
        public string? Cyan { get; set; }
        public string? Sidebar { get; set; }
        public string[]? Heat { get; set; }
    }
}
