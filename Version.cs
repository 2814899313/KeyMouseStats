// ============================================================================
//  键鼠统计 - 版本号单一来源  Version.cs
//
//  版本号只在这里定义一次:程序集属性、界面文字与 CI 校验都读它。
//
//  约定(每次交付都要做):
//    1. 补丁位 +1 —— 一般改动(新统计项、修 bug、调界面)升级第三位,例如 2.0.3 → 2.0.4;
//       只有「架构或使用方式变了」才动第二位(2.0 → 2.1),第一位留给不兼容的大改。
//    2. 同步 CHANGELOG.md 顶部新增一节(编号与这里一致)。
//    3. 同步 docs/releases/ 下新建同版本号的发布说明。
//  三处不一致时,CI 的 release.yml 会拒绝发布(tag 必须与 FileVersion 匹配)。
// ============================================================================

namespace KeyMouseStats
{
    internal static class BuildInfo
    {
        /// <summary>产品版本,例如 2.0.4。</summary>
        public const string Version = "2.0.4";
        /// <summary>程序集 / 文件版本,例如 2.0.4.0。</summary>
        public const string FileVersion = "2.0.4.0";
    }
}
