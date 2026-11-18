// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 1  PrivacyMask.cs
//
//  导出/战报用的脱敏纯函数。所有方法幂等:对已脱敏输入再调用结果不变。
//  只做字面替换,不依赖 Graphics / 文件 / 数据库。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KeyMouseStats
{
    internal static class PrivacyMask
    {
        /// <summary>产品默认:战报默认关闭打码(用户显式开启才脱敏)。</summary>
        internal const bool DefaultEnabled = false;

        /// <summary>运行时开关。由设置项驱动;默认取 DefaultEnabled。</summary>
        internal static bool Enabled = DefaultEnabled;

        internal const string HiddenSuffix = " · 已隐藏";
        internal const char MaskChar = '\uFF0A';   // ＊ 全角星号

        private static readonly string[] CategoryNames =
        {
            "编程", "游戏", "视频", "其他", "未分类", "办公", "科研", "写作", "沟通", "娱乐"
        };

        internal static string CategoryName(int categoryId)
        {
            if (categoryId < 0 || categoryId >= CategoryNames.Length) return "其他";
            return CategoryNames[categoryId];
        }

        /// <summary>窗口标题脱敏:替换为「语义类别 · 已隐藏」。关闭时原样返回。</summary>
        internal static string MaskTitle(string title, int categoryId)
        {
            if (!Enabled) return title == null ? "" : title;
            return CategoryName(categoryId) + HiddenSuffix;
        }

        /// <summary>文本脱敏:保留长度与空白结构,字面替换为 ＊。</summary>
        internal static string MaskText(string text)
        {
            if (text == null) return "";
            if (!Enabled) return text;
            StringBuilder builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                builder.Append(char.IsWhiteSpace(c) ? c : MaskChar);
            }
            return builder.ToString();
        }

        /// <summary>路径脱敏:只保留文件名。</summary>
        internal static string MaskPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (!Enabled) return path;
            try
            {
                string name = Path.GetFileName(path);
                return string.IsNullOrEmpty(name) ? "已隐藏" : name;
            }
            catch
            {
                return "已隐藏";
            }
        }

        /// <summary>批量脱敏一组标题(便于战报一次性处理)。</summary>
        internal static List<string> MaskTitles(IEnumerable<string> titles, int categoryId)
        {
            List<string> result = new List<string>();
            if (titles == null) return result;
            foreach (string title in titles) result.Add(MaskTitle(title, categoryId));
            return result;
        }
    }
}