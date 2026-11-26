using System.Text;
using Kms.Core.Model;
using Kms.Storage.Sqlite;
using Kms.Storage.Text;

namespace Kms.App;

/// <summary>
/// v4 主存档：v13 文本（完整保真，含按住时长/组合键/物理键位）。
///
/// SQLite 是事件级镜像，v3 的 13 表本身不保存这些派生明细；
/// 因此 v4 以文本为主存档，启动时优先读它，退出/定时再写回。
/// 写入采用同目录临时文件 + 原子替换，避免读到半截文件。
/// </summary>
internal static class StatsSaver
{
    public static string? LastError { get; private set; }

    public static bool Save(StatsData data)
    {
        try
        {
            Directory.CreateDirectory(DataPaths.V4Root);
            string text = TextStatsCodec.Save(data);
            string temp = DataPaths.V4Text + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, DataPaths.V4Text, overwrite: true);

            // 文本已经安全落盘；镜像同步是尽力而为，不把整体保存标记为失败。
            try
            {
                DbAggregateSync.Sync(DataPaths.V4Database, data);
            }
            catch (Exception syncError)
            {
                AppendError("SQLite 镜像同步失败：" + DbAggregateSync.Describe(syncError));
            }

            LastError = null;
            return true;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            AppendError($"{error.GetType().Name}: {error.Message}");
            return false;
        }
    }

    private static void AppendError(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(DataPaths.V4Root, "save-error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch
        {
            // 诊断写不进去也不能影响退出。
        }
    }
}
