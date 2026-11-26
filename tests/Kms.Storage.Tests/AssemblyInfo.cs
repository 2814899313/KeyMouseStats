// SQLite 测试会调用 SqliteConnection.ClearAllPools() 清理临时库；该调用是进程级的，
// 并行跑其他 SQLite 测试会偶发抢连接。M1 测试总量很小，直接串行化最稳。
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
