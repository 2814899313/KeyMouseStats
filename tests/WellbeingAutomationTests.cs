// ============================================================================
//  tests/WellbeingAutomationTests.cs  ·  v3.0 Milestone 3 / Step 2
//
//  覆盖:
//    1. wellbeing_v1 新旧格式兼容(10 段旧行 / 13 段新行);
//    2. 全屏免打扰状态映射与 fail-safe 回退;
//    3. DND 开关关闭时永不抑制;
//    4. 本地触发器:Base64 往返、JSON 载荷、脚本/Webhook 合法性、异常隔离。
//
//  纯托管用例:不建窗体、不读存档、不发起真实网络请求。
// ============================================================================

using System;
using KeyMouseStats;

internal static class WellbeingAutomationTests
{
    private static int _checks;
    private static Func<bool> _savedProbe;

    private static void Check(bool v, string n) { if (!v) throw new Exception(n); _checks++; }
    private static void Equal(long a, long e, string n) { Check(a == e, n + "(期望 " + e + ",实际 " + a + ")"); }
    private static void Same(string a, string e, string n)
    {
        Check(string.Equals(a, e, StringComparison.Ordinal), n + "(期望「" + e + "」,实际「" + a + "」)");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("WellbeingAutomationTests", Run);
    }

    private static void Run()
    {
        _savedProbe = Wellbeing.FullscreenProbe;
        try
        {
            LegacyFormat();
            RoundTrip();
            SeparatorSafety();
            SuppressionMapping();
            DndGate();
            ContinuousRespectsSuppression();
            TriggerValidation();
            PayloadShape();
            AsyncSafety();
            Console.WriteLine("WellbeingAutomationTests: " + _checks + " 项断言通过");
        }
        finally
        {
            Wellbeing.FullscreenProbe = _savedProbe;
            WellbeingSettings.FullscreenDnd = true;
            WellbeingSettings.ScriptPath = "";
            WellbeingSettings.WebhookUrl = "";
        }
    }

    /// <summary>1a. 旧版 10 段配置行必须能被读入且不破坏默认值。</summary>
    private static void LegacyFormat()
    {
        WellbeingSettings.FullscreenDnd = true;
        WellbeingSettings.ScriptPath = "sentinel";
        WellbeingSettings.WebhookUrl = "sentinel";
        WellbeingSettings.Load("0|0||0|90|30||0|22|");
        Check(true, "旧格式加载不抛异常");
        Equal(WellbeingSettings.ContinuousMinutes, 90, "旧格式连续阈值保留");
        Equal(WellbeingSettings.CooldownMinutes, 30, "旧格式冷却保留");
        Check(WellbeingSettings.FullscreenDnd, "缺失的 DND 段保持默认(开)");
        Same(WellbeingSettings.ScriptPath, "sentinel", "缺失的脚本段不覆盖既有值");
        Same(WellbeingSettings.WebhookUrl, "sentinel", "缺失的 Webhook 段不覆盖既有值");

        WellbeingSettings.Load("");
        Check(true, "空串加载不抛");
        WellbeingSettings.Load(null);
        Check(true, "null 加载不抛");
    }

    /// <summary>1b. 新格式 13 段往返。</summary>
    private static void RoundTrip()
    {
        WellbeingSettings.FullscreenDnd = false;
        WellbeingSettings.ScriptPath = @"C:\tools\提醒 脚本.bat";
        WellbeingSettings.WebhookUrl = "https://example.com/hook";
        string text = WellbeingSettings.Encode();
        string[] parts = text.Split('|');
        Equal(parts.Length, 13, "新格式应为 13 段");

        WellbeingSettings.FullscreenDnd = true;
        WellbeingSettings.ScriptPath = "";
        WellbeingSettings.WebhookUrl = "";
        WellbeingSettings.Load(text);
        Check(!WellbeingSettings.FullscreenDnd, "DND 往返一致");
        Same(WellbeingSettings.ScriptPath, @"C:\tools\提醒 脚本.bat", "脚本路径往返一致(含中文与空格)");
        Same(WellbeingSettings.WebhookUrl, "https://example.com/hook", "Webhook 往返一致");

        string again = WellbeingSettings.Encode();
        Same(again, text, "编码幂等");
    }

    /// <summary>1c. 路径里出现分隔符也不能把字段切开。</summary>
    private static void SeparatorSafety()
    {
        WellbeingSettings.ScriptPath = @"C:\a|b\c.bat";
        WellbeingSettings.WebhookUrl = "https://x/y?a=1|2";
        string text = WellbeingSettings.Encode();
        Equal(text.Split('|').Length, 13, "含分隔符的路径不得增加字段数");
        WellbeingSettings.ScriptPath = "";
        WellbeingSettings.WebhookUrl = "";
        WellbeingSettings.Load(text);
        Same(WellbeingSettings.ScriptPath, @"C:\a|b\c.bat", "含分隔符的脚本路径往返一致");
        Same(WellbeingSettings.WebhookUrl, "https://x/y?a=1|2", "含分隔符的 URL 往返一致");
    }

    /// <summary>2. shell 状态 → 是否抑制。</summary>
    private static void SuppressionMapping()
    {
        Check(!ShellNotification.IsSuppressed(ShellNotification.QunsAcceptsNotifications), "可接收通知时不抑制");
        Check(ShellNotification.IsSuppressed(ShellNotification.QunsBusy), "忙时抑制");
        Check(ShellNotification.IsSuppressed(ShellNotification.QunsRunningD3dFullScreen), "全屏 D3D 时抑制");
        Check(ShellNotification.IsSuppressed(ShellNotification.QunsPresentationMode), "演示模式时抑制");
        Check(!ShellNotification.IsSuppressed(ShellNotification.QunsNotPresent), "不在场不抑制");
        Check(!ShellNotification.IsSuppressed(ShellNotification.QunsQuietTime), "静默时段不在此列(按需求只屏蔽三种)");
        Check(!ShellNotification.IsSuppressed(ShellNotification.QunsApp), "应用态不抑制");
        Check(!ShellNotification.IsSuppressed(-1), "查询失败(-1)不抑制");

        // fail-safe:真实调用不得抛;拿不到状态时必须回退且不崩溃。
        int state = ShellNotification.QueryState();
        Check(state >= -1 && state <= 7, "查询结果在合法区间内(或 -1)");
        bool active = ShellNotification.Active();
        Check(active == true || active == false, "Active() 必须返回确定的布尔值且不抛");
    }

    /// <summary>3. DND 关闭时探针恒为 false。</summary>
    private static void DndGate()
    {
        Wellbeing.FullscreenProbe = _savedProbe;
        WellbeingSettings.FullscreenDnd = false;
        bool off = Wellbeing.FullscreenProbe();
        Check(!off, "DND 关闭时绝不抑制提醒");

        WellbeingSettings.FullscreenDnd = true;
        bool on = Wellbeing.FullscreenProbe();
        Check(on == true || on == false, "DND 开启时探针返回值确定且不抛");
    }

    /// <summary>4. 连续提醒必须尊重免打扰。</summary>
    private static void ContinuousRespectsSuppression()
    {
        WellbeingSettings.ContinuousEnabled = true;
        WellbeingSettings.ContinuousMinutes = 90;
        WellbeingSettings.CooldownMinutes = 30;
        WellbeingSettings.LastReminder = DateTime.MinValue;

        ActiveSession session = new ActiveSession();
        session.Seconds = 120 * 60;   // 2 小时

        Wellbeing.FullscreenProbe = delegate { return true; };
        Check(Wellbeing.ContinuousMessage(DateTime.Now, session) == null, "全屏/演示时久坐提醒应被挂起");
        Check(WellbeingSettings.LastReminder == DateTime.MinValue, "被挂起时不记录提醒时间");

        Wellbeing.FullscreenProbe = delegate { return false; };
        string message = Wellbeing.ContinuousMessage(DateTime.Now, session);
        Check(message != null, "非免打扰时应产生提醒");
        Check(message.IndexOf("建议休息", StringComparison.Ordinal) >= 0, "提醒文案包含建议休息");
        Check(WellbeingSettings.LastReminder != DateTime.MinValue, "产生提醒后记录时间");

        Check(Wellbeing.ContinuousMessage(DateTime.Now, null) == null, "无会话不提醒");
        Wellbeing.FullscreenProbe = _savedProbe;
    }

    /// <summary>5. 触发器参数合法性。</summary>
    private static void TriggerValidation()
    {
        Check(AutomationTrigger.HasScript(@"C:\a\b.bat"), ".bat 合法");
        Check(AutomationTrigger.HasScript(@"C:\a\b.PS1"), ".PS1 大小写不敏感");
        Check(AutomationTrigger.HasScript(@"C:\a\b.exe"), ".exe 合法");
        Check(!AutomationTrigger.HasScript(@"C:\a\b.txt"), ".txt 不合法");
        Check(!AutomationTrigger.HasScript(""), "空路径不合法");
        Check(!AutomationTrigger.HasScript(null), "null 路径不合法");

        Check(AutomationTrigger.HasWebhook("http://a/b"), "http 合法");
        Check(AutomationTrigger.HasWebhook("HTTPS://a/b"), "https 大小写不敏感");
        Check(!AutomationTrigger.HasWebhook("ftp://a"), "ftp 不合法");
        Check(!AutomationTrigger.HasWebhook(""), "空 URL 不合法");

        Check(!AutomationTrigger.Fire("", "", 90, 30), "未配置触发器时返回 false");
        Check(!AutomationTrigger.Fire("C:\\nope.txt", "not-a-url", 90, 30), "非法配置返回 false");
        Check(AutomationTrigger.Fire("C:\\nope.bat", "http://127.0.0.1:1/x", 90, 30), "合法配置应成功排程");
    }

    /// <summary>6. JSON 载荷形状固定。</summary>
    private static void PayloadShape()
    {
        Same(AutomationTrigger.BuildPayload(90, 30),
            "{\"event\":\"sedentary_alert\",\"continuous_minutes\":90,\"cooldown_minutes\":30}", "载荷逐字匹配");
        string p = AutomationTrigger.BuildPayload(0, 0);
        Check(p.IndexOf("\"event\":\"sedentary_alert\"", StringComparison.Ordinal) >= 0, "载荷含事件名");
        Check(p.StartsWith("{", StringComparison.Ordinal) && p.EndsWith("}", StringComparison.Ordinal), "载荷是合法 JSON 对象");

        Same(AutomationTrigger.EncodeField(""), "", "空串编码为空");
        Same(AutomationTrigger.DecodeField(""), "", "空串解码为空");
        Same(AutomationTrigger.DecodeField("not-base64!!"), "", "非法 Base64 解码为空而不是抛异常");
        Same(AutomationTrigger.DecodeField(AutomationTrigger.EncodeField("中文|管道")), "中文|管道", "Base64 往返一致");
    }

    /// <summary>7. 异步路径的异常隔离与超时。</summary>
    private static void AsyncSafety()
    {
        Check(!AutomationTrigger.RunScript(@"C:\definitely\missing.bat"), "不存在的脚本返回 false 且不抛");
        Check(!AutomationTrigger.RunScript(""), "空脚本返回 false");

        DateTime start = DateTime.UtcNow;
        bool posted = AutomationTrigger.PostWebhook("http://127.0.0.1:1/hook", "{}");
        double elapsed = (DateTime.UtcNow - start).TotalSeconds;
        Check(!posted, "连接被拒绝时返回 false");
        Check(elapsed < AutomationTrigger.WebhookTimeoutMs / 1000.0 + 2.0, "失败路径不得拖满超时");
        Check(AutomationTrigger.WebhookTimeoutMs == 5000, "Webhook 超时为 5000ms");
        Check(!AutomationTrigger.PostWebhook("not-a-url", "{}"), "非法 URL 直接返回 false");
    }
}
