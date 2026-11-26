using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Kms.Core.Model;
using Kms.Storage.Text;

namespace Kms.App.Reports;

/// <summary>v4 → v13 文本回滚导出（M7 / T7.4）。</summary>
internal static class LegacyExporter
{
    public static async Task<string?> SaveAsync(StatsData data, TopLevel owner)
    {
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "导出 v13 文本",
                SuggestedFileName = $"键鼠统计-v13-{DateTime.Now:yyyyMMdd-HHmm}.txt",
                DefaultExtension = "txt",
                FileTypeChoices =
                [
                    new FilePickerFileType("v13 文本") { Patterns = ["*.txt"] }
                ]
            });

        if (file is null)
        {
            return null;
        }

        string text = TextStatsCodec.Save(data);
        await using Stream stream = await file.OpenWriteAsync();
        await using StreamWriter writer = new(stream);
        await writer.WriteAsync(text);
        return file.Path.LocalPath;
    }
}
