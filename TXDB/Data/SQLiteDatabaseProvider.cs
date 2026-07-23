using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using Dapper;
using TXDB.Models;

namespace TXDB.Data
{
    /// <summary>
    /// SQLite 数据库操作实现。
    /// </summary>
    public class SQLiteDatabaseProvider : DatabaseProviderBase
    {
        /// <summary>
        /// 当前 Provider 对应的数据库类型。
        /// </summary>
        public override DatabaseProviderType ProviderType
        {
            get { return DatabaseProviderType.SQLite; }
        }

        /// <summary>
        /// 当前 Provider 的显示名称。
        /// </summary>
        public override string DisplayName
        {
            get { return "SQLite"; }
        }

        /// <summary>
        /// 创建 SQLite 连接。
        /// </summary>
        public override IDbConnection CreateConnection(ConnectionProfile profile)
        {
            return new SQLiteConnection(BuildConnectionString(profile));
        }

        /// <summary>
        /// 根据配置生成 SQLite 连接字符串。
        /// </summary>
        public override string BuildConnectionString(ConnectionProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.RawConnectionString))
            {
                return profile.RawConnectionString;
            }

            SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder();
            builder.DataSource = profile.FilePath;
            builder.Version = 3;
            builder.FailIfMissing = false;
            return builder.ConnectionString;
        }

        /// <summary>
        /// 读取 SQLite 数据库列表，文件型数据库固定显示 main。
        /// </summary>
        public override IList<DatabaseObjectNode> LoadMetadata(ConnectionProfile profile)
        {
            TestConnection(profile);
            return new List<DatabaseObjectNode>
            {
                new DatabaseObjectNode
                {
                    Name = "main",
                    Kind = DatabaseObjectKind.Database,
                    DatabaseName = "main"
                }
            };
        }

        /// <summary>
        /// 按需读取 SQLite 表、视图和字段元数据。
        /// </summary>
        public override DatabaseObjectNode LoadDatabaseMetadata(ConnectionProfile profile, string databaseName)
        {
            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                IEnumerable<dynamic> objects = connection.Query(@"
SELECT name AS ObjectName, type AS ObjectType
FROM sqlite_master
WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'
ORDER BY type, name");

                DatabaseObjectNode databaseNode = new DatabaseObjectNode
                {
                    Name = "main",
                    Kind = DatabaseObjectKind.Database,
                    DatabaseName = "main"
                };

                foreach (dynamic row in objects)
                {
                    string objectName = row.ObjectName;
                    string objectType = row.ObjectType;
                    DatabaseObjectNode tableNode = new DatabaseObjectNode
                    {
                        Name = objectName,
                        Kind = objectType == "view" ? DatabaseObjectKind.View : DatabaseObjectKind.Table,
                        DatabaseName = databaseNode.Name
                    };

                    foreach (dynamic column in connection.Query("PRAGMA table_info(" + QuoteIdentifier(objectName) + ")"))
                    {
                        tableNode.Children.Add(new DatabaseObjectNode
                        {
                            Name = column.name,
                            Kind = DatabaseObjectKind.Column,
                            DatabaseName = databaseNode.Name,
                            ParentName = objectName,
                            DataType = column.type
                        });
                    }

                    databaseNode.Children.Add(tableNode);
                }

                if (databaseNode.Children.Count == 0)
                {
                    databaseNode.Children.Add(CreateInfoNode("当前数据库没有读取到表或视图。"));
                }

                return databaseNode;
            }
        }

        /// <summary>
        /// 读取 SQLite 表字段结构。
        /// </summary>
        public override DataTable LoadTableSchema(ConnectionProfile profile, DatabaseObjectNode tableNode)
        {
            DataTable schemaTable = CreateSchemaTable();
            if (tableNode == null)
            {
                return schemaTable;
            }

            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                foreach (dynamic column in connection.Query("PRAGMA table_info(" + QuoteIdentifier(tableNode.Name) + ")"))
                {
                    DataRow row = schemaTable.NewRow();
                    row["序号"] = (int)column.cid + 1;
                    row["字段名"] = column.name;
                    row["主键"] = IsSQLitePrimaryKey(column.pk) ? "是" : string.Empty;
                    row["类型"] = column.type;
                    row["可空"] = column.notnull == 0 ? "是" : "否";
                    row["默认值"] = column.dflt_value == null ? string.Empty : column.dflt_value;
                    row["注释"] = string.Empty;
                    schemaTable.Rows.Add(row);
                }
            }

            return schemaTable;
        }

        /// <summary>
        /// 判断 SQLite PRAGMA table_info 返回的 pk 值是否表示主键。
        /// </summary>
        private static bool IsSQLitePrimaryKey(object value)
        {
            if (value == null || value == System.DBNull.Value)
            {
                return false;
            }

            int pkIndex;
            return int.TryParse(value.ToString(), out pkIndex) && pkIndex > 0;
        }

        /// <summary>
        /// 转义 SQLite 标识符。
        /// </summary>
        private static string QuoteIdentifier(string name)
        {
            return "\"" + (name ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }
    }
}
