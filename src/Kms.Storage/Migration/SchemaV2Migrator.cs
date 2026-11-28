using Kms.Core.Apps;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Migration;

/// <summary>
/// schema_rev=1 → 2 的幂等升级：只新建应用身份、别名与四张按日分桶表，
/// 不修改、不回填任何历史行。旧日期的单应用小时/VK/热区保持“未采集”。
/// </summary>
internal static class SchemaV2Migrator
{
    public static void Apply(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        foreach (string statement in Sqlite.KmsSchema.V2Statements())
        {
            Sqlite.KmsSchema.Execute(connection, statement);
        }

        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT OR IGNORE INTO app_dim " +
                    "(app_uid, app_key, display_name, canonical_path, first_seen_ms, last_seen_ms, flags) " +
                    "VALUES ($uid, $key, $name, '', 0, 0, 1);";
                command.Parameters.AddWithValue("$uid", AppIdentity.OtherAppUid);
                command.Parameters.AddWithValue("$key", AppIdentity.OtherAppKey);
                command.Parameters.AddWithValue("$name", AppIdentity.OtherAppName);
                command.ExecuteNonQuery();
            }

            Sqlite.KmsSchema.UpsertMeta(
                connection,
                transaction,
                "schema_rev",
                Sqlite.KmsSchema.Rev.ToString(System.Globalization.CultureInfo.InvariantCulture));
            transaction.Commit();
        }

        Sqlite.KmsSchema.Execute(
            connection,
            "PRAGMA user_version = " +
            Sqlite.KmsSchema.Rev.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";");
    }
}
