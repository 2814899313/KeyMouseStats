using System.Xml.Linq;

namespace Kms.Architecture.Tests;

/// <summary>
/// 依赖方向守门人（方案 §4.2）。读 csproj 的 ProjectReference，
/// 比反射更早生效：即使引用未被使用，也会在这里失败。
/// </summary>
public sealed class DependencyRuleTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Core_HasNoProjectReferencesAndNoPackages()
    {
        XDocument project = LoadProject("src", "Kms.Core");

        Assert.Empty(ProjectReferences(project));
        Assert.Empty(PackageReferences(project));
    }

    [Fact]
    public void Core_IsPlatformNeutral()
    {
        Assert.Equal("net10.0", TargetFramework(LoadProject("src", "Kms.Core")));
    }

    [Fact]
    public void Storage_DependsOnlyOnCore()
    {
        XDocument project = LoadProject("src", "Kms.Storage");

        Assert.Equal(["Kms.Core"], ProjectReferences(project));
        Assert.Equal(["Microsoft.Data.Sqlite"], PackageReferences(project));
    }

    [Fact]
    public void Input_DependsOnlyOnCore()
    {
        XDocument project = LoadProject("src", "Kms.Input");

        Assert.Equal(["Kms.Core"], ProjectReferences(project));
        Assert.Equal("net10.0-windows", TargetFramework(project));
    }

    [Fact]
    public void Render_DependsOnlyOnCoreAndAvalonia()
    {
        XDocument project = LoadProject("src", "Kms.Render");

        Assert.Equal(["Kms.Core"], ProjectReferences(project));
        Assert.Equal(["Avalonia"], PackageReferences(project));
    }

    [Fact]
    public void App_IsTheOnlyCompositionRoot()
    {
        XDocument app = LoadProject("src", "Kms.App");
        Assert.Equal(
            ["Kms.Core", "Kms.Input", "Kms.Render", "Kms.Storage"],
            ProjectReferences(app));

        foreach (string projectName in new[] { "Kms.Core", "Kms.Storage", "Kms.Input", "Kms.Render" })
        {
            XDocument project = LoadProject("src", projectName);
            Assert.DoesNotContain("Kms.App", ProjectReferences(project));
        }
    }

    private static XDocument LoadProject(string folder, string name)
    {
        string path = Path.Combine(RepoRoot, folder, name, $"{name}.csproj");
        Assert.True(File.Exists(path), $"未找到项目文件：{path}");
        return XDocument.Load(path);
    }

    private static string[] ProjectReferences(XDocument project) =>
        project.Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(
                include!.Replace('\\', Path.DirectorySeparatorChar)))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] PackageReferences(XDocument project) =>
        project.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string TargetFramework(XDocument project) =>
        project.Descendants("TargetFramework").Select(element => element.Value).Single();

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.slnx")) ||
                File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录（KeyMouseStats.sln）。");
    }
}
