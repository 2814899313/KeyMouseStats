namespace Kms.Tools.ScrubFixtures;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Dispatch(args);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            Console.Error.WriteLine($"错误：{exception}");
            return 1;
        }
    }

    private static int Dispatch(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string command = args[0];
        string[] rest = args[1..];

        return command switch
        {
            "scrub" => Scrub(rest),
            "verify" => Verify(rest),
            _ => Unknown(command)
        };
    }

    private static int Scrub(string[] args)
    {
        Options options = Options.Parse(args);
        bool force = options.Has("--force");
        string source = options.Get("--source") ?? RepoLayout.DefaultSource;
        string destination = options.Get("--dest") ?? RepoLayout.DefaultDestination;

        FixtureScrubber.Run(source, destination, force);
        FixtureManifest.Write(destination);
        Console.WriteLine($"已生成清洗固件：{Path.GetRelativePath(RepoLayout.Root, destination)}");
        return 0;
    }

    private static int Verify(string[] args)
    {
        Options options = Options.Parse(args);
        string directory = options.Get("--dir") ?? RepoLayout.DefaultDestination;

        List<string> problems = [.. FixtureManifest.Verify(directory), .. FixtureScanner.Verify(directory)];

        if (problems.Count == 0)
        {
            Console.WriteLine("固件校验通过。");
            return 0;
        }

        foreach (string problem in problems)
        {
            Console.Error.WriteLine($"  - {problem}");
        }

        Console.Error.WriteLine($"固件校验失败：{problems.Count} 个问题。");
        return 1;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知命令：{command}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            固件清洗工具（v4 T0.3）

            用法：
              scrub-fixtures scrub  [--source <dir>] [--dest <dir>] [--force]
              scrub-fixtures verify [--dir <dir>]

            默认：
              --source  _fixtures/v3      （本地真实样本，不入库）
              --dest    tests/Fixtures    （清洗后固件，入库）

            scrub 会重写目标目录并重新生成 CHECKSUMS.txt；目标非空时必须加 --force。
            verify 校验 sha256，并扫描是否仍有未脱敏的路径 / 标题 / 文本。
            """);
    }

    private sealed class Options
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public static Options Parse(string[] args)
        {
            Options options = new();

            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];

                if (!argument.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"无法识别的参数：{argument}");
                }

                if (argument is "--force")
                {
                    options._values[argument] = null;
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    throw new InvalidOperationException($"参数缺少值：{argument}");
                }

                options._values[argument] = args[++i];
            }

            return options;
        }

        public bool Has(string name) => _values.ContainsKey(name);

        public string? Get(string name) => _values.GetValueOrDefault(name);
    }
}
