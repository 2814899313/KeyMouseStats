namespace Kms.Core.IO;

/// <summary>
/// 同时识别 Windows 与 Unix 分隔符的文件名提取。
/// Kms.Core 的测试会在 Linux 上跑，不能直接用 <see cref="Path.GetFileName(string)"/>。
/// </summary>
public static class PathNames
{
    public static string FileName(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        int cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return cut >= 0 ? path[(cut + 1)..] : path;
    }

    public static string WithoutDirectory(string? path) => FileName(path);
}
