using Kms.Core.Model;
using Kms.Storage.Migration;
using Kms.Storage.Sqlite;
using Kms.Storage.Text;

namespace Kms.App;

/// <summary>
/// 首启把 v3 数据导入 v4 独立目录（方案 §9.2 / T7.3）。
/// 幂等：v4 库或文本已存在时直接跳过。
/// </summary>
internal static class FirstRunImport
{
    public static string Ensure()
    {
        try
        {
            if (File.Exists(DataPaths.V4Text) || File.Exists(DataPaths.V4Database))
            {
                return "";
            }

            if (!File.Exists(DataPaths.V3Text))
            {
                return "";
            }

            Directory.CreateDirectory(DataPaths.V4Root);

            // 文本是完整保真格式（含按住/组合键/物理键位）。
            // v3 的 stats.txt 只含当前月；更早月份在月档里，由 V2Migrator 写进 SQLite。
            File.Copy(DataPaths.V3Text, DataPaths.V4Text);

            MigrationOutcome outcome = V2Migrator.Run(DataPaths.V3TextRoot, DataPaths.V4Root);

            // 用 SQLite 的完整日期范围，加上文本里的当前月明细，生成 v4 主存档。
            try
            {
                if (!outcome.Failed && File.Exists(DataPaths.V4Database))
                {
                    StatsData merged = SqliteStatsReader.Read(DataPaths.V4Database);
                    StatsData text = TextStatsCodec.Load(File.ReadAllText(DataPaths.V4Text));
                    StatsSeedMerge.MergeDetail(merged, text);
                    StatsSaver.Save(merged);
                }
            }
            catch (Exception mergeError)
            {
                // 合并失败不阻止启动：删除未合并的文本，让 StatsLoader 回退到 49 天 SQLite，
                // 至少不丢历史日；当前月逐键明细会缺失。
                try
                {
                    File.Delete(DataPaths.V4Text);
                }
                catch
                {
                }

                return outcome.Describe() + "；文本/库合并失败：" + mergeError.Message;
            }

            return outcome.Describe();
        }
        catch (Exception error)
        {
            return "v3 导入失败：" + error.Message;
        }
    }
}
