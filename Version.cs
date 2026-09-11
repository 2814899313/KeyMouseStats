// ============================================================================
//  键鼠统计 - 版本号单一来源  Version.cs
//
//  版本号只在这里定义一次:程序集属性、界面文字与 CI 校验都读它。
//  发版时只改本文件的两个常量,以及 CHANGELOG.md 与 docs/releases/。
// ============================================================================

namespace KeyMouseStats
{
    internal static class BuildInfo
    {
        /// <summary>产品版本,例如 1.8.0。</summary>
        public const string Version = "1.8.0";
        /// <summary>程序集 / 文件版本,例如 1.8.0.0。</summary>
        public const string FileVersion = "1.8.0.0";
    }
}
