using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace KeyMouseStats
{
    // ============================================================================
    //  开机自动启动
    //
    //  为什么不再用注册表 Run 键:
    //  * Run 键记的是「勾选那一刻的 exe 全路径」。程序一旦被复制成别的文件名
    //    (KeyMouseStats-1.9.0-r7.exe 这种),注册表不会跟着变,下次登录拉起的就是
    //    一个旧文件,或者干脆和手动打开的实例抢单实例互斥量 —— 表现为「自启没生效」。
    //  * Run 键没有失败原因、没有状态查询,用户只有「登录后看到/没看到」一条反馈。
    //
    //  现在改成「当前用户的计划任务」:
    //  * 触发器同时挂「开机」与「登录后」,并打开 StartWhenAvailable ——
    //    开机时还没有交互会话(GUI 程序在 session 0 里既没有界面也收不到输入),
    //    任务会被排到用户登录的那一刻执行。对这类必须跑在交互会话里的统计程序,
    //    这才是「开机就启动」在 Windows 上真正能落地的形态。
    //  * 任务是交互身份(InteractiveToken)、普通权限,不需要管理员,也不会弹 UAC。
    //  * 每次启动调用一次 Sync():路径变了就重注册,旧 Run 键顺手清掉。
    //
    //  默认关闭:新装/首次运行不会创建任何任务,必须由用户在托盘菜单里打开。
    // ============================================================================
    internal static class AutoStart
    {
        /// <summary>任务计划程序里的任务名(根目录下)。改名会让老用户的自启失效,不要动。</summary>
        internal const string TaskName = "KeyMouseStats";

        /// <summary>1.9.0 之前用的注册表值名,只用于迁移与清理。</summary>
        internal const string LegacyRunValueName = "KeyMouseStatsWidget";

        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        // Task Scheduler 的常量,写死在这里免得为了几个 enum 去引 COM 互操作程序集。
        private const int TaskCreateOrUpdate = 6;
        private const int LogonInteractiveToken = 3;   // TASK_LOGON_INTERACTIVE_TOKEN
        private const int TriggerBoot = 8;             // TASK_TRIGGER_BOOT
        private const int TriggerLogon = 9;            // TASK_TRIGGER_LOGON
        private const int ActionExec = 0;              // TASK_ACTION_EXEC
        private const int InstancesIgnoreNew = 3;      // 已经在跑就忽略新的

        // ---------------------------------------------------------------- 公共接口

        /// <summary>任务是否存在。默认(没有任务)返回 false —— 这就是「默认关闭」。</summary>
        internal static bool IsEnabled()
        {
            object service = ConnectService();
            if (service == null) return false;
            try
            {
                object folder = GetRootFolder(service);
                if (folder == null) return false;
                return GetTask(folder) != null;
            }
            catch (Exception) { return false; }
            finally { Release(service); }
        }

        /// <summary>任务动作里登记的可执行文件路径(没登记返回空串)。</summary>
        internal static string RegisteredPath()
        {
            object service = ConnectService();
            if (service == null) return "";
            try
            {
                object folder = GetRootFolder(service);
                if (folder == null) return "";
                object task = GetTask(folder);
                if (task == null) return "";
                object definition = GetProperty(task, "Definition");
                object actions = GetProperty(definition, "Actions");
                object action = Invoke(actions, "Item", new object[] { 1 });
                if (action == null) return "";
                object path = GetProperty(action, "Path");
                return path == null ? "" : path.ToString();
            }
            catch (Exception) { return ""; }
            finally { Release(service); }
        }

        /// <summary>
        /// 打开自启:注册一个「开机 + 登录」触发的交互式任务。
        /// 失败时把原因交给调用方显示 —— 静默失败的自启比没有更糟。
        /// </summary>
        internal static bool Enable(string exePath, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                error = "找不到当前程序文件,无法登记自启。";
                return false;
            }

            object service = ConnectService();
            if (service == null)
            {
                error = "任务计划程序服务不可用(Schedule.Service)。";
                return false;
            }
            try
            {
                object folder = GetRootFolder(service);
                if (folder == null)
                {
                    error = "打不开任务计划程序的根目录。";
                    return false;
                }

                string exe = NormalizePath(exePath);
                string folderPath = Path.GetDirectoryName(exe);
                // 先试「开机 + 登录」两路触发器;个别策略不允许非管理员建开机任务,退化成登录触发。
                if (!Register(folder, exe, folderPath, true, ref error))
                {
                    if (!Register(folder, exe, folderPath, false, ref error)) return false;
                }

                DeleteLegacyRunValue();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally { Release(service); }
        }

        /// <summary>关掉自启:删任务,顺手清掉历史遗留的 Run 键。</summary>
        internal static bool Disable(out string error)
        {
            error = "";
            object service = ConnectService();
            if (service == null)
            {
                error = "任务计划程序服务不可用(Schedule.Service)。";
                return false;
            }
            try
            {
                object folder = GetRootFolder(service);
                if (folder != null)
                {
                    try { Invoke(folder, "DeleteTask", new object[] { TaskName, 0 }); }
                    catch (Exception) { /* 本来就没有这个任务 */ }
                }
                DeleteLegacyRunValue();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally { Release(service); }
        }

        /// <summary>
        /// 每次启动调一次:把「注册表里的旧自启」迁到计划任务上,并保证任务指向
        /// **当前正在运行的这个 exe**(复制改名之后自启跟着走,这是最容易失效的一环)。
        /// 任何失败都只写诊断,不影响主程序运行。
        /// </summary>
        internal static void Sync(string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;
                bool hasLegacy = HasLegacyRunValue();
                bool hasTask = IsEnabled();
                if (!hasTask && !hasLegacy) return;          // 默认关闭:什么都不做

                string registered = hasTask ? RegisteredPath() : "";
                if (hasLegacy && !hasTask)
                {
                    string error;
                    if (!Enable(exePath, out error)) Log("迁移旧自启失败: " + error);
                    return;
                }
                if (hasLegacy) DeleteLegacyRunValue();
                if (!SamePath(registered, exePath))
                {
                    string error;
                    if (!Enable(exePath, out error)) Log("更新自启路径失败: " + error);
                }
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        // ---------------------------------------------------------------- 路径判定(可测)

        /// <summary>去掉首尾引号与空白,统一大小写,便于比较两个路径是否指向同一个文件。</summary>
        internal static string NormalizePath(string path)
        {
            if (path == null) return "";
            string value = path.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2).Trim();
            try { value = Path.GetFullPath(value); }
            catch (Exception) { }
            return value;
        }

        /// <summary>两个路径是否指向同一个文件(忽略引号、大小写与相对路径写法)。</summary>
        internal static bool SamePath(string left, string right)
        {
            string a = NormalizePath(left);
            string b = NormalizePath(right);
            if (a.Length == 0 || b.Length == 0) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // ---------------------------------------------------------------- 注册表迁移

        private static bool HasLegacyRunValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return key != null && key.GetValue(LegacyRunValueName) != null;
            }
            catch (Exception) { return false; }
        }

        private static void DeleteLegacyRunValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (key.GetValue(LegacyRunValueName) != null) key.DeleteValue(LegacyRunValueName, false);
                }
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- 计划任务(COM 迟绑定)

        private static bool Register(object folder, string exe, string workingDirectory, bool withBootTrigger, ref string error)
        {
            try
            {
                object definition = Invoke(folder, "NewTask", new object[] { 0 });
                if (definition == null) { error = "创建任务定义失败。"; return false; }

                object info = GetProperty(definition, "RegistrationInfo");
                if (info != null)
                {
                    SetProperty(info, "Description", "键鼠统计:登录后自动启动(可在托盘菜单关闭)");
                    SetProperty(info, "Author", "KeyMouseStats");
                }

                object settings = GetProperty(definition, "Settings");
                if (settings != null)
                {
                    // 关键:错过开机那一刻(还没有交互会话)时,等用户一登录就补跑。
                    SetProperty(settings, "StartWhenAvailable", true);
                    SetProperty(settings, "DisallowStartIfOnBatteries", false);
                    SetProperty(settings, "StopIfGoingOnBatteries", false);
                    SetProperty(settings, "MultipleInstances", InstancesIgnoreNew);
                    SetProperty(settings, "ExecutionTimeLimit", "PT0S");
                }

                object principal = GetProperty(definition, "Principal");
                if (principal != null)
                {
                    SetProperty(principal, "LogonType", LogonInteractiveToken);
                    SetProperty(principal, "RunLevel", 0);   // 最低权限,不弹 UAC
                }

                object triggers = GetProperty(definition, "Triggers");
                if (triggers == null) { error = "无法创建触发器。"; return false; }
                if (withBootTrigger)
                {
                    object boot = Invoke(triggers, "Create", new object[] { TriggerBoot });
                    if (boot == null) { error = "无法创建开机触发器。"; return false; }
                }
                object logon = Invoke(triggers, "Create", new object[] { TriggerLogon });
                if (logon == null) { error = "无法创建登录触发器。"; return false; }

                object actions = GetProperty(definition, "Actions");
                if (actions == null) { error = "无法创建动作。"; return false; }
                object action = Invoke(actions, "Create", new object[] { ActionExec });
                if (action == null) { error = "无法创建启动动作。"; return false; }
                SetProperty(action, "Path", exe);
                if (!string.IsNullOrEmpty(workingDirectory)) SetProperty(action, "WorkingDirectory", workingDirectory);

                Invoke(folder, "RegisterTaskDefinition",
                    new object[] { TaskName, definition, TaskCreateOrUpdate, null, null, LogonInteractiveToken });
                return true;
            }
            catch (Exception ex)
            {
                error = Describe(ex);
                return false;
            }
        }

        private static object ConnectService()
        {
            try
            {
                Type type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null) return null;
                object service = Activator.CreateInstance(type);
                try { Invoke(service, "Connect", null); }
                catch (Exception) { Invoke(service, "Connect", new object[] { null, null, null, null }); }
                return service;
            }
            catch (Exception) { return null; }
        }

        private static object GetRootFolder(object service)
        {
            try { return Invoke(service, "GetFolder", new object[] { "\\" }); }
            catch (Exception) { return null; }
        }

        private static object GetTask(object folder)
        {
            try { return Invoke(folder, "GetTask", new object[] { TaskName }); }
            catch (Exception) { return null; }
        }

        private static object Invoke(object target, string name, object[] args)
        {
            if (target == null) return null;
            return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);
        }

        private static object GetProperty(object target, string name)
        {
            if (target == null) return null;
            return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
        }

        private static void SetProperty(object target, string name, object value)
        {
            if (target == null) return;
            target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new object[] { value });
        }

        private static void Release(object comObject)
        {
            try
            {
                if (comObject != null && System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
            }
            catch (Exception) { }
        }

        /// <summary>反射+COM 会把真正的 HRESULT 包两层,取最内层的消息才有用。</summary>
        private static string Describe(Exception error)
        {
            Exception inner = error;
            while (inner.InnerException != null) inner = inner.InnerException;
            return inner.Message;
        }

        private static void Log(string message)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "键鼠统计");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "diagnostics.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] 自启: " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
