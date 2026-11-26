using System.Reflection;

namespace Kms.Core;

/// <summary>
/// 版本号只从程序集特性读取；真正的唯一来源是仓库根目录的 <c>Directory.Build.props</c>。
/// </summary>
public static class BuildInfo
{
    public static string Version { get; } = ReadTrimmed<AssemblyInformationalVersionAttribute>(
        static attribute => attribute.InformationalVersion);

    public static string FileVersion { get; } = ReadTrimmed<AssemblyFileVersionAttribute>(
        static attribute => attribute.Version);

    public static string Product { get; } = ReadTrimmed<AssemblyProductAttribute>(
        static attribute => attribute.Product);

    private static string ReadTrimmed<TAttribute>(Func<TAttribute, string?> selector)
        where TAttribute : Attribute
    {
        string? value = typeof(BuildInfo).Assembly
            .GetCustomAttribute<TAttribute>() is { } attribute
            ? selector(attribute)
            : null;

        return string.IsNullOrWhiteSpace(value)
            ? "0.0.0"
            : value.Split('+', 2)[0];
    }
}
