using System.Globalization;
using Kms.Core.Apps;
using Kms.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Apps;

public sealed record AppCategorySnapshot(
    IReadOnlyList<AppCategoryDefinition> Categories,
    IReadOnlyList<AppCategoryRule> Rules);

/// <summary>
/// 自定义工作性质分类与规则的用户配置仓库。短连接读写，不与事件写线程共享连接；
/// 所有 SQL 都是强类型参数，不经过反射映射。
/// </summary>
public sealed class AppCategoryStore
{
    private readonly string _databasePath;

    public AppCategoryStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath => _databasePath;

    /// <summary>启动时加载一次的小配置快照；失败时回退内置分类。</summary>
    public AppCategorySnapshot Load()
    {
        try
        {
            using SqliteConnection connection = OpenReadWrite();
            EnsureSchema(connection);
            return ReadSnapshot(connection);
        }
        catch (SqliteException)
        {
            return new AppCategorySnapshot(AppCategoryBuiltins.All, []);
        }
    }

    public Task<AppCategorySnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Load();
        }, cancellationToken);

    public Task<AppCategoryDefinition> CreateAsync(
        string name,
        string colorHex,
        bool isWork,
        int sortOrder,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateName(name);
            string key = "custom_" + Guid.NewGuid().ToString("N")[..10];
            using SqliteConnection connection = OpenReadWrite();
            EnsureSchema(connection);
            using SqliteTransaction transaction = connection.BeginTransaction();
            int legacyId = ScalarInt(
                connection,
                transaction,
                "SELECT IFNULL(MAX(legacy_id),10)+1 FROM app_category;");
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO app_category " +
                "(category_key,name,color_hex,is_work,sort_order,is_builtin,legacy_id) " +
                "VALUES ($key,$name,$color,$work,$sort,0,$legacy);";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$name", name.Trim());
            command.Parameters.AddWithValue("$color", NormalizeColor(colorHex));
            command.Parameters.AddWithValue("$work", isWork ? 1 : 0);
            command.Parameters.AddWithValue("$sort", sortOrder);
            command.Parameters.AddWithValue("$legacy", legacyId);
            command.ExecuteNonQuery();
            transaction.Commit();
            return new AppCategoryDefinition(
                key,
                name.Trim(),
                NormalizeColor(colorHex),
                isWork,
                sortOrder,
                false,
                legacyId);
        }, cancellationToken);

    public Task UpdateAsync(
        string categoryKey,
        string name,
        string colorHex,
        bool isWork,
        int sortOrder,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateName(name);
            using SqliteConnection connection = OpenReadWrite();
            EnsureSchema(connection);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "UPDATE app_category SET name=$name,color_hex=$color,is_work=$work,sort_order=$sort " +
                "WHERE category_key=$key;";
            command.Parameters.AddWithValue("$key", categoryKey);
            command.Parameters.AddWithValue("$name", name.Trim());
            command.Parameters.AddWithValue("$color", NormalizeColor(colorHex));
            command.Parameters.AddWithValue("$work", isWork ? 1 : 0);
            command.Parameters.AddWithValue("$sort", sortOrder);
            command.ExecuteNonQuery();
        }, cancellationToken);

    /// <summary>只允许删除 custom_ 开头的自定义分类；规则回落到未分类。</summary>
    public Task<bool> DeleteAsync(
        string categoryKey,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (categoryKey.StartsWith("custom_", StringComparison.Ordinal) is false)
            {
                return false;
            }

            using SqliteConnection connection = OpenReadWrite();
            EnsureSchema(connection);
            using SqliteTransaction transaction = connection.BeginTransaction();
            using (SqliteCommand deleteRules = connection.CreateCommand())
            {
                deleteRules.Transaction = transaction;
                deleteRules.CommandText =
                    "UPDATE app_category_rule SET category_key='uncategorized',category_id=4 " +
                    "WHERE category_key=$key;";
                deleteRules.Parameters.AddWithValue("$key", categoryKey);
                deleteRules.ExecuteNonQuery();
            }

            int changed;
            using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM app_category WHERE category_key=$key AND is_builtin=0;";
                delete.Parameters.AddWithValue("$key", categoryKey);
                changed = delete.ExecuteNonQuery();
            }

            transaction.Commit();
            return changed > 0;
        }, cancellationToken);

    public Task UpsertRuleAsync(
        string ruleKey,
        string categoryKey,
        string source,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using SqliteConnection connection = OpenReadWrite();
            EnsureSchema(connection);
            using SqliteTransaction transaction = connection.BeginTransaction();
            int categoryId = ScalarInt(
                connection,
                transaction,
                "SELECT IFNULL((SELECT legacy_id FROM app_category WHERE category_key=$key),4);",
                ("$key", categoryKey));
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO app_category_rule (rule_key,category_id,category_key,source,updated_ms) " +
                "VALUES ($rule,$id,$key,$source,$updated) " +
                "ON CONFLICT(rule_key) DO UPDATE SET " +
                "category_id=excluded.category_id,category_key=excluded.category_key," +
                "source=excluded.source,updated_ms=excluded.updated_ms;";
            command.Parameters.AddWithValue("$rule", ruleKey);
            command.Parameters.AddWithValue("$id", categoryId);
            command.Parameters.AddWithValue("$key", categoryKey);
            command.Parameters.AddWithValue("$source", source);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
            transaction.Commit();
        }, cancellationToken);

    public Task DeleteRuleAsync(string ruleKey, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using SqliteConnection connection = OpenReadWrite();
            EnsureSchema(connection);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM app_category_rule WHERE rule_key=$rule;";
            command.Parameters.AddWithValue("$rule", ruleKey);
            command.ExecuteNonQuery();
        }, cancellationToken);

    private static AppCategorySnapshot ReadSnapshot(SqliteConnection connection)
    {
        List<AppCategoryDefinition> categories = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT category_key,name,color_hex,is_work,sort_order,is_builtin,legacy_id " +
                "FROM app_category ORDER BY sort_order, name;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                categories.Add(new AppCategoryDefinition(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3) != 0,
                    reader.GetInt32(4),
                    reader.GetInt64(5) != 0,
                    reader.GetInt32(6)));
            }
        }

        List<AppCategoryRule> rules = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT rule_key,category_key,category_id,source,updated_ms FROM app_category_rule " +
                "ORDER BY rule_key;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rules.Add(new AppCategoryRule(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? "user_override" : reader.GetString(3),
                    reader.GetInt64(4)));
            }
        }

        if (categories.Count == 0)
        {
            categories.AddRange(AppCategoryBuiltins.All);
        }

        return new AppCategorySnapshot(categories, rules);
    }

    private SqliteConnection OpenReadWrite()
    {
        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=3000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        KmsSchema.Ensure(connection, "4.2.0");
    }

    private static int ScalarInt(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] values)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in values)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("分类名称不能为空。", nameof(name));
        }

        if (name.Trim().Length > 24)
        {
            throw new ArgumentException("分类名称不能超过 24 个字符。", nameof(name));
        }
    }

    private static string NormalizeColor(string color)
    {
        string value = string.IsNullOrWhiteSpace(color) ? "#8B5CF6" : color.Trim();
        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        if (value.Length != 7)
        {
            return "#8B5CF6";
        }

        for (int i = 1; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                return "#8B5CF6";
            }
        }

        return value.ToUpperInvariant();
    }
}
