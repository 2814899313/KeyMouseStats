// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 2  AutomationTrigger.cs
//
//  久坐提醒的本地触发器:异步执行用户脚本或 POST Webhook。
//  铁律:绝不阻塞调用线程(250ms 定时器 / UI 线程);任何失败都只记诊断、不抛。
// ============================================================================

using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;

namespace KeyMouseStats
{
    internal static class AutomationTrigger
    {
        /// <summary>Webhook 超时(毫秒)。</summary>
        internal const int WebhookTimeoutMs = 5000;

        /// <summary>诊断日志出口;产品里接到 Program.LogEvent,测试里可替换。</summary>
        internal static Action<string> Log = delegate { };

        /// <summary>把任意文本编码成不含分隔符的 Base64,供 wellbeing_v1 使用。</summary>
        internal static string EncodeField(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try
            {
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            }
            catch { return ""; }
        }

        /// <summary>解码;非法输入返回空串,绝不抛。</summary>
        internal static string DecodeField(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try
            {
                byte[] bytes = Convert.FromBase64String(text);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }

        /// <summary>固定字段顺序的 JSON 载荷(确定性,便于测试逐字比对)。</summary>
        internal static string BuildPayload(int continuousMinutes, int cooldownMinutes)
        {
            return "{\"event\":\"sedentary_alert\",\"continuous_minutes\":"
                + continuousMinutes.ToString(CultureInfo.InvariantCulture)
                + ",\"cooldown_minutes\":"
                + cooldownMinutes.ToString(CultureInfo.InvariantCulture) + "}";
        }

        internal static bool HasScript(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string lower = path.ToLowerInvariant();
            return lower.EndsWith(".bat") || lower.EndsWith(".cmd")
                || lower.EndsWith(".ps1") || lower.EndsWith(".exe");
        }

        internal static bool HasWebhook(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 异步投递。调用线程只做参数检查与排程,实际 IO 在后台线程。
        /// 返回 false 表示没有任何触发器被排程(未配置或格式不合法)。
        /// </summary>
        internal static bool Fire(string scriptPath, string webhookUrl, int continuousMinutes, int cooldownMinutes)
        {
            bool script = HasScript(scriptPath);
            bool webhook = HasWebhook(webhookUrl);
            if (!script && !webhook) return false;
            string payload = BuildPayload(continuousMinutes, cooldownMinutes);
            string scriptCopy = script ? scriptPath : null;
            string urlCopy = webhook ? webhookUrl : null;
            try
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        if (scriptCopy != null) RunScript(scriptCopy);
                        if (urlCopy != null) PostWebhook(urlCopy, payload);
                    }
                    catch (Exception error) { SafeLog("触发器异常:" + error.GetType().Name); }
                });
                return true;
            }
            catch (Exception error)
            {
                SafeLog("触发器排程失败:" + error.GetType().Name);
                return false;
            }
        }

        /// <summary>同步执行脚本(供后台线程与测试调用)。失败返回 false。</summary>
        internal static bool RunScript(string path)
        {
            if (!HasScript(path)) return false;
            // .bat/.ps1 是通过 cmd.exe / powershell.exe 间接启动的,宿主存在不代表脚本存在,
            // 必须先自己确认文件在,否则「脚本已删」会被误判成执行成功。
            try { if (!System.IO.File.Exists(path)) return false; }
            catch { return false; }
            try
            {
                ProcessStartInfo info;
                if (path.ToLowerInvariant().EndsWith(".ps1"))
                    info = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\"");
                else if (path.ToLowerInvariant().EndsWith(".bat") || path.ToLowerInvariant().EndsWith(".cmd"))
                    info = new ProcessStartInfo("cmd.exe", "/c \"" + path + "\"");
                else
                    info = new ProcessStartInfo(path);
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                using (Process process = Process.Start(info))
                {
                    if (process == null) return false;
                    SafeLog("触发器已执行脚本");
                    return true;
                }
            }
            catch (Exception error)
            {
                SafeLog("脚本执行失败:" + error.GetType().Name);
                return false;
            }
        }

        /// <summary>同步 POST(供后台线程与测试调用)。失败返回 false,永不抛。</summary>
        internal static bool PostWebhook(string url, string payload)
        {
            if (!HasWebhook(url)) return false;
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Timeout = WebhookTimeoutMs;
                request.ReadWriteTimeout = WebhookTimeoutMs;
                byte[] body = Encoding.UTF8.GetBytes(payload == null ? "" : payload);
                request.ContentLength = body.Length;
                using (System.IO.Stream stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    int code = (int)response.StatusCode;
                    SafeLog("触发器 Webhook 返回 " + code);
                    return code >= 200 && code < 300;
                }
            }
            catch (Exception error)
            {
                SafeLog("Webhook 失败:" + error.GetType().Name);
                return false;
            }
        }

        private static void SafeLog(string message)
        {
            try { Action<string> sink = Log; if (sink != null) sink(message); }
            catch { }
        }
    }
}