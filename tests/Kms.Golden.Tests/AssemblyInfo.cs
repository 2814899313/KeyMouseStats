// Avalonia headless/Skia 的渲染后端是进程级的，多个测试类并行初始化会互相踩状态。
// golden 测试量很小，串行化最稳。
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
