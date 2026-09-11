using System;
using System.IO;
using System.Reflection;

namespace KeyMouseStats.Tests
{
    /// <summary>
    /// 测试失败诊断助手。控制台在部分区域设置下无法打印中文异常(会输出
    /// 「由于 Exception.ToString() 失败」),这时把异常写到临时文件再读出来。
    /// 用法:TestDiagnostics.Run("SomeTests", delegate { SomeTests.Run(); });
    /// 任何用例都可以在排查问题时临时套一层,不影响正常退出码。
    /// </summary>
    internal static class TestDiagnostics
    {
        /// <summary>跑一个用例主体;失败时把完整异常写进临时文件并打印路径,同时返回非零退出码。</summary>
        public static int Run(string name, Action body)
        {
            try
            {
                body();
                return 0;
            }
            catch (Exception error)
            {
                string path = Path.Combine(Path.GetTempPath(), "test-failure-" + name + ".log");
                try { File.WriteAllText(path, error.ToString()); } catch { }
                Console.WriteLine("FAILED: " + name + " · " + error.GetType().Name + " · 详情见 " + path);
                return 1;
            }
        }

        /// <summary>按类型名反射一个用例的 Main(等价于 test.bat 的入口约定),便于手工重跑单个用例。</summary>
        public static int RunReflected(string typeName)
        {
            Type type = typeof(TestDiagnostics).Assembly.GetType(typeName, true);
            MethodInfo main = type.GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            return main == null ? 2 : Run(typeName, delegate { main.Invoke(null, null); });
        }
    }
}
