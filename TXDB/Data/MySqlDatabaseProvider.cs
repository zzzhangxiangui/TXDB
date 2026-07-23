using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using MySqlConnector;
using TXDB.Models;

namespace TXDB.Data
{
    /// <summary>
    /// MySQL 数据库操作实现，使用 MySqlConnector 驱动直接创建连接。
    /// </summary>
    public class MySqlDatabaseProvider : DatabaseProviderBase
    {
        /// <summary>
        /// 当前 Provider 对应的数据库类型。
        /// </summary>
        public override DatabaseProviderType ProviderType
        {
            get { return DatabaseProviderType.MySQL; }
        }

        /// <summary>
        /// 当前 Provider 的显示名称。
        /// </summary>
        public override string DisplayName
        {
            get { return "MySQL"; }
        }

        /// <summary>
        /// 创建 MySQL 连接。
        /// </summary>
        public override IDbConnection CreateConnection(ConnectionProfile profile)
        {
            return new MySqlConnection(BuildConnectionString(profile));
        }

        /// <summary>
        /// 根据连接配置生成 MySQL 连接字符串。
        /// </summary>
        public override string BuildConnectionString(ConnectionProfile profile)
        {
            if (profile == null)
            {
                throw new InvalidOperationException("MySQL 连接配置不能为空。");
            }

            if (!string.IsNullOrWhiteSpace(profile.RawConnectionString))
            {
                return profile.RawConnectionString;
            }

            MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder();
            builder.Server = profile.Host;
            builder.Port = ParsePort(profile.Port);
            builder.UserID = profile.UserName;
            builder.Password = profile.Password;
            builder.CharacterSet = "utf8mb4";
            builder.ConnectionTimeout = 8;
            builder.ConvertZeroDateTime = true;
            builder.AllowPublicKeyRetrieval = true;
            builder.AllowUserVariables = true;

            string databaseName = NormalizeDatabaseName(profile.DatabaseName);
            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                builder.Database = databaseName;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// 读取当前账号可访问的 MySQL 用户数据库列表，不主动加载每个库的表结构。
        /// </summary>
        public override IList<DatabaseObjectNode> LoadMetadata(ConnectionProfile profile)
        {
            ConnectionProfile metadataProfile = profile == null ? null : profile.Clone();
            if (metadataProfile != null)
            {
                metadataProfile.DatabaseName = string.Empty;
            }

            using (IDbConnection connection = CreateConnection(metadataProfile))
            {
                connection.Open();
                List<string> databaseNames = connection.Query<string>(@"
SELECT SCHEMA_NAME
FROM INFORMATION_SCHEMA.SCHEMATA
WHERE SCHEMA_NAME NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
ORDER BY SCHEMA_NAME").ToList();

                if (databaseNames.Count == 0)
                {
                    return new List<DatabaseObjectNode>
                    {
                        CreateInfoNode("当前连接没有读取到可访问的用户数据库。")
                    };
                }

                return databaseNames
                    .Select(databaseName => new DatabaseObjectNode
                    {
                        Name = databaseName,
                        Kind = DatabaseObjectKind.Database,
                        DatabaseName = databaseName
                    })
                    .ToList();
            }
        }

        /// <summary>
        /// 按需读取指定 MySQL 数据库下的表、视图和字段元数据。
        /// </summary>
        public override DatabaseObjectNode LoadDatabaseMetadata(ConnectionProfile profile, string databaseName)
        {
            string safeDatabaseName = string.IsNullOrWhiteSpace(databaseName)
                ? (profile == null ? string.Empty : profile.DatabaseName)
                : databaseName;
            if (string.IsNullOrWhiteSpace(safeDatabaseName))
            {
                throw new InvalidOperationException("请选择需要打开的 MySQL 数据库。");
            }

            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                List<dynamic> tables = connection.Query(@"
SELECT
    TABLE_SCHEMA AS DatabaseName,
    TABLE_NAME AS ObjectName,
    TABLE_TYPE AS ObjectType,
    TABLE_COMMENT AS CommentText
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = @DatabaseName
  AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')
ORDER BY TABLE_TYPE, TABLE_NAME", new { DatabaseName = safeDatabaseName }).ToList();

                List<dynamic> columns = connection.Query(@"
SELECT
    TABLE_SCHEMA AS DatabaseName,
    TABLE_NAME AS TableName,
    COLUMN_NAME AS ColumnName,
    DATA_TYPE AS DataType,
    COLUMN_TYPE AS ColumnType,
    COLUMN_COMMENT AS CommentText
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @DatabaseName
ORDER BY TABLE_NAME, ORDINAL_POSITION", new { DatabaseName = safeDatabaseName }).ToList();

                return BuildDatabaseNode(safeDatabaseName, tables, columns);
            }
        }

        /// <summary>
        /// 读取 MySQL 表字段结构、主键、默认值和注释。
        /// </summary>
        public override DataTable LoadTableSchema(ConnectionProfile profile, DatabaseObjectNode tableNode)
        {
            DataTable schemaTable = CreateSchemaTable();
            if (tableNode == null)
            {
                return schemaTable;
            }

            string schemaName = string.IsNullOrWhiteSpace(tableNode.SchemaName) ? tableNode.DatabaseName : tableNode.SchemaName;
            if (string.IsNullOrWhiteSpace(schemaName) && profile != null)
            {
                schemaName = profile.DatabaseName;
            }

            if (string.IsNullOrWhiteSpace(schemaName))
            {
                return schemaTable;
            }

            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                IEnumerable<dynamic> rows = connection.Query(@"
SELECT
    c.ORDINAL_POSITION AS Ordinal,
    c.COLUMN_NAME AS ColumnName,
    CASE WHEN pk.COLUMN_NAME IS NULL THEN 'N' ELSE 'Y' END AS IsPrimaryKey,
    c.DATA_TYPE AS DataType,
    c.COLUMN_TYPE AS ColumnType,
    c.IS_NULLABLE AS NullableValue,
    c.COLUMN_DEFAULT AS DefaultValue,
    c.COLUMN_COMMENT AS CommentText
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN (
    SELECT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
    INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
        ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
       AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
       AND tc.TABLE_NAME = kcu.TABLE_NAME
       AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
) pk ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA
    AND pk.TABLE_NAME = c.TABLE_NAME
    AND pk.COLUMN_NAME = c.COLUMN_NAME
WHERE c.TABLE_SCHEMA = @SchemaName AND c.TABLE_NAME = @ObjectName
ORDER BY c.ORDINAL_POSITION", new { SchemaName = schemaName, ObjectName = tableNode.Name });

                foreach (dynamic row in rows)
                {
                    DataRow dataRow = schemaTable.NewRow();
                    dataRow["序号"] = row.Ordinal;
                    dataRow["字段名"] = row.ColumnName;
                    dataRow["主键"] = ReadText(row, "IsPrimaryKey") == "Y" ? "是" : string.Empty;
                    dataRow["类型"] = BuildTypeName(row);
                    dataRow["可空"] = ReadText(row, "NullableValue") == "YES" ? "是" : "否";
                    dataRow["默认值"] = ReadText(row, "DefaultValue");
                    dataRow["注释"] = ReadText(row, "CommentText");
                    schemaTable.Rows.Add(dataRow);
                }
            }

            return schemaTable;
        }

        /// <summary>
        /// 将 MySQL 元数据整理成数据库对象节点。
        /// </summary>
        private static DatabaseObjectNode BuildDatabaseNode(string databaseName, List<dynamic> tables, List<dynamic> columns)
        {
            DatabaseObjectNode databaseNode = new DatabaseObjectNode
            {
                Name = databaseName,
                Kind = DatabaseObjectKind.Database,
                DatabaseName = databaseName
            };

            if (tables == null || tables.Count == 0)
            {
                databaseNode.Children.Add(CreateInfoNode("当前数据库没有读取到表或视图。"));
                return databaseNode;
            }

            foreach (dynamic tableRow in tables)
            {
                string objectName = ReadText(tableRow, "ObjectName");
                string objectType = ReadText(tableRow, "ObjectType");
                DatabaseObjectKind kind = SameName(objectType, "VIEW") ? DatabaseObjectKind.View : DatabaseObjectKind.Table;
                DatabaseObjectNode tableNode = new DatabaseObjectNode
                {
                    Name = objectName,
                    Kind = kind,
                    DatabaseName = databaseName,
                    SchemaName = databaseName,
                    Comment = ReadText(tableRow, "CommentText")
                };

                foreach (dynamic columnRow in columns.Where(column => SameName(ReadText(column, "TableName"), objectName)))
                {
                    tableNode.Children.Add(new DatabaseObjectNode
                    {
                        Name = ReadText(columnRow, "ColumnName"),
                        Kind = DatabaseObjectKind.Column,
                        DatabaseName = databaseName,
                        SchemaName = databaseName,
                        ParentName = objectName,
                        DataType = ReadText(columnRow, "ColumnType"),
                        Comment = ReadText(columnRow, "CommentText")
                    });
                }

                databaseNode.Children.Add(tableNode);
            }

            return databaseNode;
        }

        /// <summary>
        /// 根据 MySQL 字段元数据生成可读类型名称。
        /// </summary>
        private static string BuildTypeName(dynamic row)
        {
            string columnType = ReadText(row, "ColumnType");
            if (!string.IsNullOrWhiteSpace(columnType))
            {
                return columnType;
            }

            return ReadText(row, "DataType");
        }

        /// <summary>
        /// 从 Dapper 动态结果中按忽略大小写方式读取文本值。
        /// </summary>
        private static string ReadText(dynamic row, string columnName)
        {
            IDictionary<string, object> values = row as IDictionary<string, object>;
            if (values == null)
            {
                return string.Empty;
            }

            foreach (KeyValuePair<string, object> pair in values)
            {
                if (string.Equals(pair.Key, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value == null ? string.Empty : pair.Value.ToString();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 解析 MySQL 端口，未填写时使用默认 3306。
        /// </summary>
        private static uint ParsePort(string portText)
        {
            if (string.IsNullOrWhiteSpace(portText))
            {
                return 3306;
            }

            uint port;
            if (!uint.TryParse(portText, out port) || port == 0 || port > 65535)
            {
                throw new InvalidOperationException("MySQL 端口必须是 1 到 65535 之间的数字。");
            }

            return port;
        }

        /// <summary>
        /// 规范 MySQL 初始数据库名称，过滤从 SQL Server 默认配置遗留过来的 master。
        /// </summary>
        private static string NormalizeDatabaseName(string databaseName)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return string.Empty;
            }

            string safeDatabaseName = databaseName.Trim();
            return SameName(safeDatabaseName, "master") ? string.Empty : safeDatabaseName;
        }
    }
}
