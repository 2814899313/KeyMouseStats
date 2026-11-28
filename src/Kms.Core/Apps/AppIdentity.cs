namespace Kms.Core.Apps;

/// <summary>
/// 应用身份的稳定文本约定。当前阶段只做进程路径归一化，不按同名 exe 自动合并；
/// 路径变化通过 app_alias 显式归并，避免 python.exe / node.exe 之类的误并。
/// </summary>
public static class AppIdentity
{
    public const string OtherAppKey = "__other__";
    public const string OtherAppName = "其他应用（单日上限）";
    public const int OtherAppUid = -1;

    /// <summary>v4 统一口径：进程路径去首尾空白后 ToLowerInvariant。</summary>
    public static string NormalizeKey(string? processPath) =>
        string.IsNullOrWhiteSpace(processPath)
            ? ""
            : processPath.Trim().ToLowerInvariant();

    public static string DisplayName(string? processPath, string? processName = null)
    {
        if (!string.IsNullOrWhiteSpace(processName))
        {
            return processName.Trim();
        }

        if (string.IsNullOrWhiteSpace(processPath))
        {
            return "未识别应用";
        }

        string name = Path.GetFileName(processPath.Trim());
        return string.IsNullOrWhiteSpace(name) ? processPath.Trim() : name;
    }

    public static bool IsOther(string? appKey) =>
        string.Equals(OtherAppKey, appKey, StringComparison.Ordinal);
}
