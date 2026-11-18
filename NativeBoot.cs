// ============================================================================
//  键鼠统计 v3.0  Milestone 1  NativeBoot.cs
//  原生 SQLite 引擎(e_sqlite3.dll)的释放与装载引导。
//
//  为什么需要它:
//   本项目是「单文件绿色免安装」,安装包只有一个 exe,不能要求用户旁放 DLL。
//   因此把 x64/x86 两份原生库作为**托管资源**内嵌进主 exe,首次运行时幂等释放到
//   %LOCALAPPDATA%\KeyMouseStats\native\<引擎版本>\<架构>\,再显式 LoadLibrary。
//
//  失败模型(硬要求):
//   任何一步失败(资源缺失、目录不可写、杀软拦截、架构不符)都只记录诊断并返回
//   false,**绝不抛异常、绝不崩溃**。调用方(StatsStoreFactory)据此降级回 v13 文本。
//
//  清爽版(CLEAN_EDITION)不内嵌任何资源(CI 闸门要求资源数=0),因此本文件在
//  CLEAN_EDITION 下编译为只报告「不可用」的桩。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace KeyMouseStats
{
#if !CLEAN_EDITION
    internal static class NativeBoot
    {
        /// <summary>引擎版本标识;参与释放路径,升级引擎时改这里即可并行存放新旧两份。</summary>
        internal const string NativeVersion = "e_sqlite3-3.41.2";

        /// <summary>主 exe 里内嵌的原生库资源名(必须与 build.bat 的 /resource: 名字一致)。</summary>
        internal const string ResourceX64 = "KmsNativeX64";
        internal const string ResourceX86 = "KmsNativeX86";

        /// <summary>释放出来的文件名。必须叫这个名字,DllImport 才能命中已装载模块。</summary>
        internal const string NativeFileName = "e_sqlite3.dll";

        private static readonly object Gate = new object();
        private static bool _attempted;
        private static bool _ok;
        private static string _diagnosis = "尚未尝试装载";
        private static string _lastAction = "none";
        private static string _nativePath = "";
        private static IntPtr _handle = IntPtr.Zero;
        private static string _rootOverride;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string fileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);

        /// <summary>
        /// 测试专用:重定向数据根目录到 %TEMP% 沙盒。
        /// 产品代码不要设置它,默认走 %LOCALAPPDATA%\KeyMouseStats。
        /// </summary>
        internal static string RootOverride
        {
            get { return _rootOverride; }
            set
            {
                lock (Gate)
                {
                    _rootOverride = value;
                    // 根目录变了,之前的装载结论不再适用。
                    _attempted = false;
                    _ok = false;
                    _diagnosis = "尚未尝试装载";
                    _lastAction = "none";
                    _nativePath = "";
                }
            }
        }

        internal static bool Available
        {
            get
            {
                TryEnsure();
                return _ok;
            }
        }

        internal static string Diagnosis
        {
            get
            {
                TryEnsure();
                return _diagnosis;
            }
        }

        /// <summary>最近一次释放动作:extracted / reused / failed / none。供测试断言幂等。</summary>
        internal static string LastAction
        {
            get
            {
                TryEnsure();
                return _lastAction;
            }
        }

        internal static IntPtr Handle
        {
            get
            {
                TryEnsure();
                return _handle;
            }
        }

        /// <summary>已装载的 DLL 完整路径(装载失败时为空串)。</summary>
        internal static string NativeFilePath
        {
            get
            {
                TryEnsure();
                return _nativePath;
            }
        }

        /// <summary>应释放到的目录:<root>\native\<版本>\<架构>。测试可在装载前断言它。</summary>
        internal static string NativeDirectory()
        {
            return Path.Combine(Path.Combine(Path.Combine(DataRoot(), "native"), NativeVersion), ArchName());
        }

        /// <summary>产品的默认数据根:%LOCALAPPDATA%\KeyMouseStats。</summary>
        internal static string DataRoot()
        {
            if (!string.IsNullOrEmpty(_rootOverride)) return _rootOverride;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeyMouseStats");
        }

        internal static string ArchName()
        {
            return IntPtr.Size == 8 ? "x64" : "x86";
        }

        internal static string ResourceName()
        {
            return IntPtr.Size == 8 ? ResourceX64 : ResourceX86;
        }

        /// <summary>幂等装载。返回 true 表示引擎已就绪。</summary>
        internal static bool TryEnsure()
        {
            lock (Gate)
            {
                if (_attempted) return _ok;
                _attempted = true;
                try
                {
                    _ok = LoadCore();
                }
                catch (Exception error)
                {
                    _ok = false;
                    _lastAction = "failed";
                    _diagnosis = "装载异常: " + error.GetType().Name + " · " + error.Message;
                }
                return _ok;
            }
        }

        private static bool LoadCore()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resource = ResourceName();
            Stream embedded = assembly.GetManifestResourceStream(resource);
            if (embedded == null)
            {
                _lastAction = "failed";
                _diagnosis = "主程序里没有内嵌原生资源 " + resource
                    + "(清爽版或未经 build.bat 编译的产物),按设计降级回文本存储。";
                return false;
            }

            long expected;
            string directory = NativeDirectory();
            string target = Path.Combine(directory, NativeFileName);
            using (embedded)
            {
                expected = embedded.Length;
                if (!Extract(embedded, expected, directory, target))
                    return false;
            }

            _nativePath = target;
            IntPtr handle = LoadLibraryW(target);
            if (handle == IntPtr.Zero)
            {
                int code = Marshal.GetLastWin32Error();
                _lastAction = "failed";
                _diagnosis = "LoadLibrary 失败(win32=" + code + ") path=" + target
                    + ";按设计降级回文本存储。";
                return false;
            }

            _handle = handle;
            _diagnosis = "原生引擎已装载 v" + NativeVersion + " (" + ArchName() + "," + _lastAction + ") path=" + target;
            return true;
        }

        /// <summary>
        /// 释放内嵌资源。<paramref name="target"/> 已存在且大小一致时直接复用 ——
        /// 这就是幂等性的落点:重复启动不会重写文件,也不会因为杀软锁文件而失败。
        /// </summary>
        private static bool Extract(Stream source, long expected, string directory, string target)
        {
            try
            {
                if (File.Exists(target) && new FileInfo(target).Length == expected)
                {
                    _lastAction = "reused";
                    _diagnosis = "复用已释放的原生库";
                    return true;
                }
            }
            catch
            {
                // 读不到大小就当不存在,走重写。
            }

            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception error)
            {
                _lastAction = "failed";
                _diagnosis = "无法创建原生库目录 " + directory + " · " + error.Message;
                return false;
            }

            string temp = target + ".tmp";
            try
            {
                using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[81920];
                    for (; ; )
                    {
                        int read = source.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        output.Write(buffer, 0, read);
                    }
                    output.Flush();
                }

                long written = new FileInfo(temp).Length;
                if (written != expected)
                {
                    TryDelete(temp);
                    _lastAction = "failed";
                    _diagnosis = "原生库释放不完整(" + written + "/" + expected + " 字节) path=" + temp;
                    return false;
                }

                if (File.Exists(target)) TryDelete(target);
                File.Move(temp, target);
                _lastAction = "extracted";
                return true;
            }
            catch (Exception error)
            {
                TryDelete(temp);
                _lastAction = "failed";
                _diagnosis = "释放原生库失败 " + target + " · " + error.GetType().Name + " · " + error.Message;
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        /// <summary>测试用:卸载并复位,便于验证「再次装载」路径。产品不调用。</summary>
        internal static void ReleaseForTest()
        {
            lock (Gate)
            {
                if (_handle != IntPtr.Zero)
                {
                    try { FreeLibrary(_handle); }
                    catch { }
                    _handle = IntPtr.Zero;
                }
                _attempted = false;
                _ok = false;
                _diagnosis = "尚未尝试装载";
                _lastAction = "none";
                _nativePath = "";
            }
        }
    }
#else
    /// <summary>
    /// 清爽版桩:不内嵌原生资源,永远报告不可用,让 StatsStoreFactory 直接走文本后端。
    /// 保留同名成员是为了让上层代码在两种版本下都能编译。
    /// </summary>
    internal static class NativeBoot
    {
        internal const string NativeVersion = "e_sqlite3-3.41.2";

        internal static bool Available { get { return false; } }
        internal static string Diagnosis { get { return "清爽版不内嵌原生 SQLite 引擎"; } }
        internal static string LastAction { get { return "none"; } }
        internal static string NativeFilePath { get { return ""; } }

        internal static bool TryEnsure() { return false; }

        internal static string NativeDirectory() { return ""; }

        internal static string DataRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeyMouseStats");
        }

        internal static string ArchName() { return IntPtr.Size == 8 ? "x64" : "x86"; }

        internal static void ReleaseForTest() { }
    }
#endif
}
