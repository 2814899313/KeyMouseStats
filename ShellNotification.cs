// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 2  ShellNotification.cs
//
//  系统级免打扰状态查询(shell32!SHQueryUserNotificationState)。
//  API 不可用时无缝回退到既有的 FullscreenWindow 几何检测。
// ============================================================================

using System;
using System.Runtime.InteropServices;

namespace KeyMouseStats
{
    internal static class ShellNotification
    {
        internal const int QunsNotPresent = 1;
        internal const int QunsBusy = 2;
        internal const int QunsRunningD3dFullScreen = 3;
        internal const int QunsPresentationMode = 4;
        internal const int QunsAcceptsNotifications = 5;
        internal const int QunsQuietTime = 6;
        internal const int QunsApp = 7;

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        /// <summary>查询系统状态;失败返回 -1(调用方据此回退)。</summary>
        internal static int QueryState()
        {
            try
            {
                int state;
                if (SHQueryUserNotificationState(out state) == 0) return state;
            }
            catch { }
            return -1;
        }

        /// <summary>该状态下是否应静默挂起提醒:忙 / 全屏 D3D / 演示模式。</summary>
        internal static bool IsSuppressed(int state)
        {
            return state == QunsBusy
                || state == QunsRunningD3dFullScreen
                || state == QunsPresentationMode;
        }

        /// <summary>产品入口:先问系统,拿不到答案再退回几何全屏检测。</summary>
        internal static bool Active()
        {
            int state = QueryState();
            if (state > 0) return IsSuppressed(state);
            try { return FullscreenWindow.Active(); } catch { return false; }
        }
    }
}