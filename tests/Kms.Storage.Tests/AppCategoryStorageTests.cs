using System.Globalization;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Storage.Apps;
using Kms.Storage.Sqlite;
using Kms.Storage.Text;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Tests;

/// <summary>
/// v4.2.0 自定义工作性质分类：SQLite 强类型配置读写、历史数字规则兼容与删除回落。
/// </summary>
public sealed class AppCategoryStorageTests
{
    [Fact]
    public async Task Store_Crud_PersistsCustomCategoryAndRules()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            AppCategoryStore store = new(path);
            AppCategorySnapshot seed = store.Load();
            Assert.Equal(AppCategoryBuiltins.All.Count, seed.Categories.Count);
            Assert.Empty(seed.Rules);

            AppCategoryDefinition created = await store.CreateAsync(
                "小说创作",
                "EC4899",
                isWork: true,
                sortOrder: 20);
            Assert.StartsWith("custom_", created.CategoryKey, StringComparison.Ordinal);
            Assert.Equal("#EC4899", created.ColorHex);
            Assert.True(created.LegacyId >= 11);

            AppCategoryDefinition second = await store.CreateAsync(
                "AI 协同（自定义）",
                "#22C55E",
                isWork: false,
                sortOrder: 21);
            Assert.NotEqual(created.LegacyId, second.LegacyId);

            await store.UpdateAsync(
                created.CategoryKey,
                "小说与长文",
                "#F472B6",
                isWork: false,
                sortOrder: 22);

            await store.UpsertRuleAsync(
                "title:c:\\apps\\goink.exe\n第",
                created.CategoryKey,
                "user_override");

            AppCategorySnapshot afterRule = store.Load();
            AppCategoryDefinition reloaded = Assert.Single(
                afterRule.Categories,
                category => category.CategoryKey == created.CategoryKey);
            Assert.Equal("小说与长文", reloaded.Name);
            Assert.Equal("#F472B6", reloaded.ColorHex);
            Assert.False(reloaded.IsWork);
            Assert.Equal(22, reloaded.SortOrder);

            AppCategoryRule rule = Assert.Single(
                afterRule.Rules,
                item => item.RuleKey == "title:c:\\apps\\goink.exe\n第");
            Assert.Equal(created.CategoryKey, rule.CategoryKey);
            Assert.Equal(created.LegacyId, rule.CategoryId);

            AppCategoryResolver resolver = new(afterRule.Categories, afterRule.Rules);
            AppCategoryMatch match = resolver.Resolve(
                @"C:\Apps\Goink.exe",
                "第一章 初入江湖");
            Assert.Equal(created.CategoryKey, match.Category.CategoryKey);
            Assert.Equal("关键词规则", match.Reason);

            Assert.True(await store.DeleteAsync(created.CategoryKey));
            Assert.False(await store.DeleteAsync(AppCategoryBuiltins.Coding));

            AppCategorySnapshot afterDelete = store.Load();
            Assert.DoesNotContain(
                afterDelete.Categories,
                category => category.CategoryKey == created.CategoryKey);
            AppCategoryRule fallback = Assert.Single(
                afterDelete.Rules,
                item => item.RuleKey == "title:c:\\apps\\goink.exe\n第");
            Assert.Equal(AppCategoryBuiltins.Uncategorized, fallback.CategoryKey);
            Assert.Equal(AppCategoryBuiltins.UncategorizedCategory.LegacyId, fallback.CategoryId);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Store_LoadsHistoricalNumericRules_AndResolverMapsThemToBuiltinKeys()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            AppCategoryStore store = new(path);
            _ = store.Load();

            using (SqliteConnection connection = Open(path))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO app_category_rule (rule_key, category_id, source, updated_ms) " +
                    "VALUES ('app:c:\\apps\\goink.exe', 7, 'user_override', 1);";
                command.ExecuteNonQuery();
            }

            AppCategorySnapshot snapshot = store.Load();
            AppCategoryRule legacy = Assert.Single(snapshot.Rules);
            Assert.Equal(AppCategoryBuiltins.Writing, legacy.CategoryKey);
            Assert.Equal(7, legacy.CategoryId);

            AppCategoryResolver resolver = new(snapshot.Categories, snapshot.Rules);
            AppCategoryMatch match = resolver.Resolve(@"C:\Apps\Goink.exe", "");
            Assert.Equal(AppCategoryBuiltins.Writing, match.Category.CategoryKey);
            Assert.True(match.Category.IsWork);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Store_RuleUpsert_ReplacesLegacyNumericRule()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            AppCategoryStore store = new(path);
            _ = store.Load();

            using (SqliteConnection connection = Open(path))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO app_category_rule (rule_key, category_id, source, updated_ms) " +
                    "VALUES ('app:c:\\apps\\goink.exe', 7, 'user_override', 1);";
                command.ExecuteNonQuery();
            }

            AppCategorySnapshot before = store.Load();
            AppCategoryRule legacy = Assert.Single(before.Rules);
            Assert.Equal(AppCategoryBuiltins.Writing, legacy.CategoryKey);

            await store.UpsertRuleAsync(
                "app:c:\\apps\\goink.exe",
                AppCategoryBuiltins.Ai,
                "user_override");

            AppCategorySnapshot after = store.Load();
            AppCategoryRule upserted = Assert.Single(after.Rules);
            Assert.Equal(AppCategoryBuiltins.Ai, upserted.CategoryKey);
            Assert.Equal(10, upserted.CategoryId);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void TextAppRule_LegacyKey_RemainsReadable_AndCustomKeyRoundTrips()
    {
        AppRule legacy = new(@"app:c:\apps\goink.exe", 7);
        Assert.True(V13Codecs.TryDecodeAppRule(
            V13Codecs.EncodeAppRule(legacy),
            out AppRule? decoded));
        Assert.Equal(7, decoded!.CategoryId);
        Assert.Equal("", decoded.CategoryKey);

        AppRule custom = new(@"app:c:\apps\goink.exe", 12, "novel");
        Assert.True(V13Codecs.TryDecodeAppRule(
            V13Codecs.EncodeAppRule(custom),
            out AppRule? roundTrip));
        Assert.Equal(12, roundTrip!.CategoryId);
        Assert.Equal("novel", roundTrip.CategoryKey);

        string legacyText = V13Codecs.EncodeText(@"app:c:\apps\goink.exe") + "|9";
        Assert.True(V13Codecs.TryDecodeAppRule(legacyText, out AppRule? nine));
        Assert.Equal(9, nine!.CategoryId);

        string outOfRange = V13Codecs.EncodeText(@"app:c:\apps\goink.exe") + "|10";
        Assert.False(V13Codecs.TryDecodeAppRule(outOfRange, out _));
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
        connection.Open();
        return connection;
    }

    private static string NewDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "kms-app-category-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
