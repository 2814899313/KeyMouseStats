// ============================================================================
//  键鼠统计 - AI 皮肤生图提示词模板  SkinPrompt.cs
//
//  四套预设都内置同一组构图约束:
//    16:9 宽屏 / 主体偏右避开左侧导航 / 顶部 20% 暗调留白衬托白字 /
//    景深虚化与低碎纹理,保证前景折线图、柱状图可读。
// ============================================================================

using System;
using System.Collections.Generic;

namespace KeyMouseStats
{
    internal sealed class SkinPromptPreset
    {
        public string Key;
        public string Name;
        public string SubjectZh;
        public string SubjectEn;
        public string ColorsZh;
        public string ColorsEn;
    }

    internal sealed class SkinPromptPair
    {
        public string Chinese;
        public string English;
    }

    internal sealed class IconPromptPreset
    {
        public string Key;
        public string Name;
        public string SubjectZh;
        public string SubjectEn;
        public string ColorsZh;
        public string ColorsEn;
    }

    internal static class SkinPrompt
    {
        internal static readonly SkinPromptPreset[] Presets =
        {            new SkinPromptPreset
            {
                Key = "cyber",
                Name = "赛博驾驶舱 · 暗夜公路",
                SubjectZh = "雨夜中的赛博驾驶舱，窗外是霓虹流动的高速公路",
                SubjectEn = "a cyberpunk cockpit at night overlooking a rain-slicked neon highway",
                ColorsZh = "深靛蓝、霓虹青、少量洋红点缀",
                ColorsEn = "deep indigo, neon cyan, restrained magenta accents"
            },
            new SkinPromptPreset
            {
                Key = "ink",
                Name = "古风水墨 · 远山云海",
                SubjectZh = "水墨晕染的远山与翻涌云海，一叶孤舟停在雾中",
                SubjectEn = "ink-wash distant mountains and a sea of clouds, a lone boat resting in the mist",
                ColorsZh = "墨黑、月白、黛青、一点朱砂",
                ColorsEn = "ink black, moon white, slate blue, a trace of cinnabar"
            },
            new SkinPromptPreset
            {
                Key = "geometry",
                Name = "极简几何 · 磨砂光影",
                SubjectZh = "悬浮的磨砂几何体与斜切光影，留出安静的空间",
                SubjectEn = "floating frosted geometric volumes with slanted light and quiet negative space",
                ColorsZh = "石墨灰、冷白、琥珀微光",
                ColorsEn = "graphite gray, cool white, faint amber glow"
            },
            new SkinPromptPreset
            {
                Key = "study",
                Name = "温馨书房 · 深夜台灯",
                SubjectZh = "深夜书房的一角，暖色台灯照亮书页与木质桌面",
                SubjectEn = "a quiet late-night study corner, a warm desk lamp lighting open books and a wooden desk",
                ColorsZh = "深棕、暖琥珀、奶白",
                ColorsEn = "deep brown, warm amber, creamy white"
            }
        };

        internal static SkinPromptPreset Find(string key)
        {
            for (int i = 0; i < Presets.Length; i++)
                if (string.Equals(Presets[i].Key, key, StringComparison.Ordinal)) return Presets[i];
            return Presets[0];
        }

        internal static readonly IconPromptPreset[] IconPresets =
        {
            new IconPromptPreset
            {
                Key = "badge",
                Name = "扁平矢量徽章",
                SubjectZh = "一枚简洁的几何徽章，外圈圆角方框与中心抽象符号",
                SubjectEn = "a clean geometric badge with a rounded square frame and an abstract central symbol",
                ColorsZh = "深蓝底、亮青主色、奶白描边",
                ColorsEn = "deep blue base, bright cyan primary, cream outline"
            },
            new IconPromptPreset
            {
                Key = "neon",
                Name = "霓虹赛博发光图标",
                SubjectZh = "一只赛博仪表盘图标，弧形刻度与发光指针",
                SubjectEn = "a cyberpunk gauge icon with an arc scale and a glowing needle",
                ColorsZh = "纯黑底、霓虹青、电紫高光",
                ColorsEn = "pure black base, neon cyan, electric violet highlights"
            },
            new IconPromptPreset
            {
                Key = "seal",
                Name = "国风朱砂印章图标",
                SubjectZh = "一枚朱砂印章风格的方印，粗边框与几何篆刻纹样",
                SubjectEn = "a cinnabar seal-style square stamp with a bold border and geometric engraved motif",
                ColorsZh = "朱砂红、暖金、墨黑",
                ColorsEn = "cinnabar red, warm gold, ink black"
            }
        };

        internal static IconPromptPreset FindIcon(string key)
        {
            for (int i = 0; i < IconPresets.Length; i++)
                if (string.Equals(IconPresets[i].Key, key, StringComparison.Ordinal)) return IconPresets[i];
            return IconPresets[0];
        }

        internal static SkinPromptPair BuildIcon(string presetKey, string subject, string colors)
        {
            return BuildIcon(FindIcon(presetKey), subject, colors);
        }

        internal static SkinPromptPair BuildIcon(IconPromptPreset preset, string subject, string colors)
        {
            if (preset == null) preset = IconPresets[0];
            bool customSubject = !string.IsNullOrEmpty(subject);
            bool customColors = !string.IsNullOrEmpty(colors);
            if (!customSubject) subject = preset.SubjectZh;
            if (!customColors) colors = preset.ColorsZh;
            string englishSubject = customSubject ? subject : preset.SubjectEn;
            string englishColors = customColors ? colors : preset.ColorsEn;

            string chinese =
                "1:1 正方形图标 / 徽章设计，单一居中主体，构图紧凑，四周留出约 10% 安全边距。\n"
                + "图标主体：" + subject + "。\n"
                + "主色调：" + colors + "。\n"
                + "粗轮廓、高对比度、扁平化色块；图形在小尺寸 24×24 px 下仍然清晰可辨，避免细线、碎点与复杂渐变。\n"
                + "背景必须是纯黑 #000000、无纹理、无阴影、无渐变、无文字、无水印、无边框光晕，便于一键去除黑底。\n"
                + "主体边缘干净、闭合，居中单一主体；不要场景、不要透视、不要多个主体。\n"
                + "负面提示：白底、灰底、渐变背景、纹理、文字、水印、多个主体、细碎线条、过曝、复杂光影。";

            string english =
                "1:1 square icon / badge design, single centered subject, compact composition with about 10% safe margin.\n"
                + "Icon subject: " + englishSubject + ".\n"
                + "Color palette: " + englishColors + ".\n"
                + "Bold outlines, high contrast, flat color blocks; the silhouette must stay legible at 24x24 px. Avoid hairlines, tiny specks and complex gradients.\n"
                + "Background must be pure black #000000, with no texture, shadow, gradient, text, watermark or glow, so it can be removed with one click.\n"
                + "Clean closed edges, one centered subject; no scene, no perspective, no multiple subjects.\n"
                + "Negative prompt: white background, gray background, gradient background, texture, text, watermark, multiple subjects, thin lines, blown highlights, complex lighting. --ar 1:1";

            return new SkinPromptPair { Chinese = chinese, English = english };
        }

        internal static SkinPromptPair Build(string presetKey, string subject, string colors)
        {
            return Build(Find(presetKey), subject, colors);
        }

        internal static SkinPromptPair Build(SkinPromptPreset preset, string subject, string colors)
        {
            if (preset == null) preset = Presets[0];
            bool customSubject = !string.IsNullOrEmpty(subject);
            bool customColors = !string.IsNullOrEmpty(colors);
            if (!customSubject) subject = preset.SubjectZh;
            if (!customColors) colors = preset.ColorsZh;
            string englishSubject = customSubject ? subject : preset.SubjectEn;
            string englishColors = customColors ? colors : preset.ColorsEn;

            string chinese =
                "16:9 宽屏横版桌面壁纸，专为键鼠统计面板设计，整体暗调、低饱和、低对比。\n"
                + "画面主体：" + subject + "。主体偏右，放置在画面右侧 60% 区域；左侧 35% 保持干净留白，避开左侧导航栏与数据卡片。\n"
                + "顶部 20% 为低对比度暗色留白区，用于衬托白色标题文字。\n"
                + "主色调：" + colors + "。\n"
                + "景深虚化，柔和光晕，低碎纹理；避免高频噪点、密集文字、logo、水印、锐利小物件和强烈高光，"
                + "确保前景折线图、柱状图与白色文字始终清晰可读。\n"
                + "电影级光影，超广角，高品质，8K。\n"
                + "负面提示：文字、水印、logo、杂乱纹理、过曝、高对比碎点、复杂边缘。";

            string english =
                "16:9 widescreen desktop wallpaper for a keyboard and mouse statistics dashboard; dark, desaturated, low-contrast mood.\n"
                + "Main subject: " + englishSubject + ". Place the subject in the right 60% of the frame; keep the left 35% clean and uncluttered for the navigation sidebar and data cards.\n"
                + "Reserve the top 20% as a low-contrast dark negative-space band for white title text.\n"
                + "Color palette: " + englishColors + ".\n"
                + "Shallow depth of field, soft glow, low-frequency texture; avoid high-frequency noise, dense text, logos, watermarks, sharp small objects and blown highlights so the foreground line charts, bars and white text stay clearly readable.\n"
                + "Cinematic lighting, ultra-wide composition, high quality, 8K. Aspect ratio 16:9. --ar 16:9\n"
                + "Negative prompt: text, watermark, logo, clutter, blown highlights, sharp small objects.";

            return new SkinPromptPair { Chinese = chinese, English = english };
        }
    }
}
