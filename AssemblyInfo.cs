using System.Reflection;
#if CLEAN_EDITION
[assembly: AssemblyTitle("键鼠统计 · 清爽版")]
[assembly: AssemblyProduct("KeyMouseStats Clean")]
[assembly: AssemblyDescription("键鼠统计 " + KeyMouseStats.BuildInfo.Version + " 清爽版")]
#else
[assembly: AssemblyTitle("键鼠统计")]
[assembly: AssemblyProduct("KeyMouseStats")]
[assembly: AssemblyDescription("键鼠统计正式版 " + KeyMouseStats.BuildInfo.Version)]
#endif
[assembly: AssemblyVersion(KeyMouseStats.BuildInfo.FileVersion)]
[assembly: AssemblyFileVersion(KeyMouseStats.BuildInfo.FileVersion)]
#if CLEAN_EDITION
[assembly: AssemblyInformationalVersion(KeyMouseStats.BuildInfo.Version + "-clean")]
#else
[assembly: AssemblyInformationalVersion(KeyMouseStats.BuildInfo.Version)]
#endif
