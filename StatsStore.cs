// ============================================================================
//  键鼠统计 v3.0  Milestone 1  StatsStore.cs
//  存储后端接缝(IStatsStore)：把「统计事实写到哪里」从调用方手里抽出来,
//  使完整版(SQLite 事件流)与清爽版(v13 文本)互不拖累。
//
//  设计约束:
//   1. 必须用 C# 5 语法(裸 csc.exe v4.0.30319,非 Roslyn);
//      不得出现 $"…" / ?. / => 表达式体 / nameof / out var / 元组。
//   2. CLEAN_EDITION 下不得出现任何 SQLite 类型引用,否则清爽版会链上
//      不存在的程序集,并破坏 build.yml 的「≤2 MiB + 零内嵌托管资源」闸门。
//   3. 本文件不产生任何副作用:不创建目录、不读写文件。数据根目录由调用方
//      传入,测试据此重定向到 %TEMP% 沙盒。
// ============================================================================

using System;

namespace KeyMouseStats
{
    /// <summary>一条前台窗口会话事实(对应 events_window_session 一行)。</summary>
    internal sealed class WindowSessionRow
    {
        public long StartMs = 0;
        public long EndMs = 0;
        public string Day = "";
        public string ProcessPath = "";
        public string ProcessName = "";
        public string WindowTitle = "";
        public long KeyCount = 0;
        public long LeftClicks = 0;
        public long RightClicks = 0;
        public long MiddleClicks = 0;
        public long XClicks = 0;
        public long Wheel = 0;
        public double MovePx = 0;
        public long ActiveMs = 0;
        public long RimeChars = 0;
        public int CategoryId = -1;
        public string CategorySource = "none";
        public string SemanticTag = "";
        public int IsOpen = 0;
    }

    /// <summary>点击热区的一个格子：通道 × 格子坐标(对应 daily_click_grid 一行)。</summary>
    internal sealed class ClickCellRow
    {
        public int Channel = 0;
        public int CellX = 0;
        public int CellY = 0;
        public int ScreenId = 0;
        public long Count = 0;
    }

    /// <summary>
    /// 存储后端接缝。完整版的实现是 SqliteStatsStore(见 KmsDb.cs),
    /// 清爽版与降级路径只有 TextStatsStore。
    /// </summary>
    internal interface IStatsStore
    {
        /// <summary>后端标识:"sqlite" / "text"。用于日志与诊断,不参与分支判断。</summary>
        string BackendId { get; }
        /// <summary>该后端当前能否真正读写。文本后端恒为 true(它是降级底线)。</summary>
        bool Available { get; }
        /// <summary>人可读的诊断说明:为什么用了这个后端。</summary>
        string Diagnosis { get; }
        /// <summary>当前 schema 版本;文本后端没有 schema,返回 0。</summary>
        int SchemaRev { get; }
        /// <summary>初始化。dataRoot 必须是可写目录(测试传 %TEMP% 沙盒)。</summary>
        bool Initialize(string dataRoot);
        /// <summary>释放连接。必须可重复调用。</summary>
        void Shutdown();
        /// <summary>自检:数据库可读、PRAGMA 生效。失败时 detail 带原因。</summary>
        bool HealthCheck(out string detail);
        /// <summary>写入一条窗口会话事实,返回自增 id;不支持时返回 -1。</summary>
        long InsertWindowSession(WindowSessionRow row);
        /// <summary>把一批热区格子累加进当天;返回实际合并的格子数。</summary>
        int MergeClickCells(string day, ClickCellRow[] cells);
    }

    /// <summary>
    /// v13 文本后端。它不接管既有 Store 的生命周期(那套逻辑仍在 KeyMouseWidget.cs 里),
    /// 只作为接缝上的降级占位:SQLite 不可用时,产品继续跑原有的 stats.txt 通道,
    /// 由此实现「原生库加载失败也绝不崩溃」。
    /// </summary>
    internal sealed class TextStatsStore : IStatsStore
    {
        private readonly string _diagnosis;
        private string _root = "";

        internal TextStatsStore(string diagnosis)
        {
            _diagnosis = diagnosis == null ? "" : diagnosis;
        }

        public string BackendId { get { return "text"; } }
        public bool Available { get { return true; } }
        public string Diagnosis { get { return _diagnosis.Length == 0 ? "v13 文本后端" : _diagnosis; } }
        public int SchemaRev { get { return 0; } }

        public bool Initialize(string dataRoot)
        {
            _root = dataRoot == null ? "" : dataRoot;
            return true;
        }

        public void Shutdown()
        {
            _root = "";
        }

        public bool HealthCheck(out string detail)
        {
            detail = "文本后端无 schema,接口可用;数据根=" + _root;
            return true;
        }

        public long InsertWindowSession(WindowSessionRow row)
        {
            // 文本后端不承载事件流:Step 3 的会话状态机会继续写 DayRecord.Apps。
            return -1;
        }

        public int MergeClickCells(string day, ClickCellRow[] cells)
        {
            return 0;
        }
    }

    /// <summary>
    /// 后端选择器。顺序永远是:SQLite(可用时)→ 文本(降级)。
    /// 任何异常都在这里被吞掉并转成 Diagnosis,绝不让初始化失败冒泡到 UI 线程。
    /// </summary>
    internal static class StatsStoreFactory
    {
        private static readonly object Gate = new object();
        private static IStatsStore _current;

        /// <summary>最近一次 Open 的结果;未调用过时为 null。</summary>
        internal static IStatsStore Current
        {
            get { return _current; }
        }

        internal static IStatsStore Open(string dataRoot)
        {
            lock (Gate)
            {
#if CLEAN_EDITION
                _current = new TextStatsStore("清爽版不内置数据库引擎(CLEAN_EDITION)");
                _current.Initialize(dataRoot);
                return _current;
#else
                string diagnosis = "未尝试";
                if (NativeBoot.TryEnsure())
                {
                    try
                    {
                        SqliteStatsStore sqlite = new SqliteStatsStore();
                        if (sqlite.Initialize(dataRoot))
                        {
                            _current = sqlite;
                            return _current;
                        }
                        diagnosis = "SQLite 初始化失败: " + sqlite.Diagnosis;
                        sqlite.Shutdown();
                    }
                    catch (Exception error)
                    {
                        diagnosis = "SQLite 初始化异常: " + error.GetType().Name + " · " + error.Message;
                    }
                }
                else
                {
                    diagnosis = "原生 SQLite 不可用: " + NativeBoot.Diagnosis;
                }
                TextStatsStore fallback = new TextStatsStore(diagnosis);
                fallback.Initialize(dataRoot);
                _current = fallback;
                return _current;
#endif
            }
        }

        /// <summary>测试用:清掉单例,避免用例之间互相污染。</summary>
        internal static void ResetForTest()
        {
            lock (Gate)
            {
                if (_current != null)
                {
                    try { _current.Shutdown(); }
                    catch { }
                }
                _current = null;
            }
        }
    }
}
