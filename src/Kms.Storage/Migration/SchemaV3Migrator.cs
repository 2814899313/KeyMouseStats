using System.Globalization;
using Kms.Core.Apps;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Migration;

/// <summary>
/// schema_rev=2 → 3：新增自定义工作性质分类表，给历史规则补充 category_key，
/// 并写入内置分类。只做增量迁移，不改写历史统计与既有规则键。
/// </summary>
internal static class SchemaV3Migrator
{
    public static void Apply(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        EnsureCategoryKeyColumn(connection);
        foreach (string statement in Sqlite.KmsSchema.V3Statements())
        {
            Sqlite.KmsSchema.Execute(connection, statement);
        }

        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            foreach (AppCategoryDefinition category in AppCategoryBuiltins.All)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT OR IGNORE INTO app_category " +
                    "(category_key,name,color_hex,is_work,sort_order,is_builtin,legacy_id) " +
                    "VALUES ($key,$name,$color,$work,$sort,$builtin,$legacy);";
                insert.Parameters.AddWithValue("$key", category.CategoryKey);
                insert.Parameters.AddWithValue("$name", category.Name);
                insert.Parameters.AddWithValue("$color", category.ColorHex);
                insert.Parameters.AddWithValue("$work", category.IsWork ? 1 : 0);
                insert.Parameters.AddWithValue("$sort", category.SortOrder);
                insert.Parameters.AddWithValue("$builtin", category.IsBuiltin ? 1 : 0);
                insert.Parameters.AddWithValue("$legacy", category.LegacyId);
                insert.ExecuteNonQuery();

                using SqliteCommand backfill = connection.CreateCommand();
                backfill.Transaction = transaction;
                backfill.CommandText =
                    "UPDATE app_category_rule SET category_key=$key " +
                    "WHERE (category_key IS NULL OR category_key='') AND category_id=$id;";
                backfill.Parameters.AddWithValue("$key", category.CategoryKey);
                backfill.Parameters.AddWithValue("$id", category.LegacyId);
                backfill.ExecuteNonQuery();
            }

            MergeLegacyEntertainment(connection, transaction);

            Sqlite.KmsSchema.UpsertMeta(
                connection,
                transaction,
                "schema_rev",
                Sqlite.KmsSchema.Rev.ToString(CultureInfo.InvariantCulture));
            transaction.Commit();
        }

        Sqlite.KmsSchema.Execute(
            connection,
            "PRAGMA user_version = " +
            Sqlite.KmsSchema.Rev.ToString(CultureInfo.InvariantCulture) + ";");
    }

    /// <summary>
    /// 旧版把「影音娱乐」和「娱乐」拆成两项；统一合并到 media(2)，
    /// 只删除内置娱乐分类，历史 9 号数字通过 AppCategoryBuiltins.LegacyAliases 继续可读。
    /// </summary>
    private static void MergeLegacyEntertainment(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand updateRules = connection.CreateCommand();
        updateRules.Transaction = transaction;
        updateRules.CommandText =
            "UPDATE app_category_rule SET category_id=$media, category_key='media' " +
            "WHERE category_key='entertainment' OR " +
            "(category_key='' AND category_id=$legacy);";
        updateRules.Parameters.AddWithValue("$media", AppCategoryBuiltins.LegacyIdFor(AppCategoryBuiltins.Media));
        updateRules.Parameters.AddWithValue("$legacy", 9);
        updateRules.ExecuteNonQuery();

        using SqliteCommand delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText =
            "DELETE FROM app_category WHERE category_key='entertainment' AND is_builtin=1;";
        delete.ExecuteNonQuery();
    }

    private static void EnsureCategoryKeyColumn(SqliteConnection connection)
    {
        bool found = false;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(app_category_rule);";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "category_key", StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            Sqlite.KmsSchema.Execute(
                connection,
                "ALTER TABLE app_category_rule ADD COLUMN category_key TEXT NOT NULL DEFAULT '';");
        }
    }
}
