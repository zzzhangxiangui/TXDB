using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Dapper;
using TXDB.Models;

namespace TXDB.Data
{
    /// <summary>
    /// SQL Server 数据库操作实现。
    /// </summary>
    public class SqlServerDatabaseProvider : DatabaseProviderBase
    {
        /// <summary>
        /// 当前 Provider 对应的数据库类型。
        /// </summary>
        public override DatabaseProviderType ProviderType
        {
            get { return DatabaseProviderType.SqlServer; }
        }

        /// <summary>
        /// 当前 Provider 的显示名称。
        /// </summary>
        public override string DisplayName
        {
            get { return "SQL Server"; }
        }

        /// <summary>
        /// 创建 SQL Server 连接。
        /// </summary>
        public override IDbConnection CreateConnection(ConnectionProfile profile)
        {
            return new SqlConnection(BuildConnectionString(profile));
        }

        /// <summary>
        /// 根据配置生成 SQL Server 连接字符串。
        /// </summary>
        public override string BuildConnectionString(ConnectionProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.RawConnectionString))
            {
                return profile.RawConnectionString;
            }

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder.DataSource = string.IsNullOrWhiteSpace(profile.Port) ? profile.Host : profile.Host + "," + profile.Port;
            builder.InitialCatalog = string.IsNullOrWhiteSpace(profile.DatabaseName) ? "master" : profile.DatabaseName;
            builder.IntegratedSecurity = profile.UseWindowsAuthentication;
            builder.ConnectTimeout = 8;

            if (!profile.UseWindowsAuthentication)
            {
                builder.UserID = profile.UserName;
                builder.Password = profile.Password;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// 读取 SQL Server 可访问的用户数据库列表，不主动加载每个库的表结构。
        /// </summary>
        public override IList<DatabaseObjectNode> LoadMetadata(ConnectionProfile profile)
        {
            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();

                List<string> databaseNames = connection.Query<string>(@"
SELECT name
FROM sys.databases
WHERE state = 0
  AND HAS_DBACCESS(name) = 1
  AND database_id > 4
  AND name NOT IN ('master', 'model', 'msdb', 'tempdb')
ORDER BY name").ToList();

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
        /// 按需读取指定 SQL Server 数据库下的架构、表、视图和字段元数据。
        /// </summary>
        public override DatabaseObjectNode LoadDatabaseMetadata(ConnectionProfile profile, string databaseName)
        {
            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                try
                {
                    return LoadSqlServerDatabaseMetadata(connection, databaseName);
                }
                catch (Exception ex)
                {
                    return CreateErrorDatabaseNode(databaseName, ex.Message);
                }
            }
        }

        /// <summary>
        /// 读取 SQL Server 字段结构、主键和扩展属性注释。
        /// </summary>
        public override DataTable LoadTableSchema(ConnectionProfile profile, DatabaseObjectNode tableNode)
        {
            DataTable schemaTable = CreateSchemaTable();
            if (tableNode == null)
            {
                return schemaTable;
            }

            string databaseName = !string.IsNullOrWhiteSpace(tableNode.DatabaseName)
                ? tableNode.DatabaseName
                : profile == null ? string.Empty : profile.DatabaseName;
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return schemaTable;
            }

            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                string databasePrefix = QuoteSqlServerIdentifier(databaseName);
                IEnumerable<dynamic> rows = connection.Query(@"
SELECT
    c.column_id AS Ordinal,
    c.name AS ColumnName,
    CASE WHEN pk.column_id IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS IsPrimaryKey,
    t.name AS DataType,
    c.max_length AS MaxLength,
    c.precision AS PrecisionValue,
    c.scale AS ScaleValue,
    c.is_nullable AS IsNullable,
    dc.definition AS DefaultValue,
    CAST(ep.value AS NVARCHAR(4000)) AS CommentText
FROM " + databasePrefix + @".sys.columns c
INNER JOIN " + databasePrefix + @".sys.objects o ON c.object_id = o.object_id
INNER JOIN " + databasePrefix + @".sys.schemas s ON o.schema_id = s.schema_id
INNER JOIN " + databasePrefix + @".sys.types t ON c.user_type_id = t.user_type_id
LEFT JOIN " + databasePrefix + @".sys.default_constraints dc ON c.default_object_id = dc.object_id
LEFT JOIN " + databasePrefix + @".sys.extended_properties ep ON ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
LEFT JOIN (
    SELECT ic.object_id, ic.column_id
    FROM " + databasePrefix + @".sys.indexes i
    INNER JOIN " + databasePrefix + @".sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    WHERE i.is_primary_key = 1
) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
WHERE s.name = @SchemaName AND o.name = @ObjectName
ORDER BY c.column_id", new { SchemaName = tableNode.SchemaName, ObjectName = tableNode.Name });

                foreach (dynamic row in rows)
                {
                    DataRow dataRow = schemaTable.NewRow();
                    dataRow["序号"] = row.Ordinal;
                    dataRow["字段名"] = row.ColumnName;
                    dataRow["主键"] = Convert.ToBoolean(row.IsPrimaryKey) ? "是" : string.Empty;
                    dataRow["类型"] = BuildSqlServerTypeName(row.DataType, row.MaxLength, row.PrecisionValue, row.ScaleValue);
                    dataRow["可空"] = row.IsNullable ? "是" : "否";
                    dataRow["默认值"] = row.DefaultValue == null ? string.Empty : row.DefaultValue;
                    dataRow["注释"] = row.CommentText == null ? string.Empty : row.CommentText;
                    schemaTable.Rows.Add(dataRow);
                }
            }

            return schemaTable;
        }

        /// <summary>
        /// 根据单个数据库的表和字段元数据构造树节点。
        /// </summary>
        private static DatabaseObjectNode LoadSqlServerDatabaseMetadata(IDbConnection connection, string databaseName)
        {
            string databasePrefix = QuoteSqlServerIdentifier(databaseName);
            List<dynamic> tables = connection.Query(@"
SELECT
    TABLE_SCHEMA AS SchemaName,
    TABLE_NAME AS ObjectName,
    TABLE_TYPE AS ObjectType
FROM " + databasePrefix + @".INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')
ORDER BY TABLE_SCHEMA, TABLE_NAME").ToList();

            List<dynamic> columns = connection.Query(@"
SELECT
    s.name AS SchemaName,
    o.name AS TableName,
    c.name AS ColumnName,
    t.name AS DataType,
    c.max_length AS MaxLength,
    c.precision AS PrecisionValue,
    c.scale AS ScaleValue,
    CAST(ep.value AS NVARCHAR(4000)) AS CommentText
FROM " + databasePrefix + @".sys.columns c
INNER JOIN " + databasePrefix + @".sys.objects o ON c.object_id = o.object_id
INNER JOIN " + databasePrefix + @".sys.schemas s ON o.schema_id = s.schema_id
INNER JOIN " + databasePrefix + @".sys.types t ON c.user_type_id = t.user_type_id
LEFT JOIN " + databasePrefix + @".sys.extended_properties ep ON ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
WHERE o.type IN ('U', 'V')
ORDER BY s.name, o.name, c.column_id").ToList();

            return BuildDatabaseNode(databaseName, tables, columns);
        }

        /// <summary>
        /// 将单个数据库的查询结果整理成统一元数据树。
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

            foreach (IGrouping<string, dynamic> schemaGroup in tables.GroupBy(row => (string)row.SchemaName).OrderBy(group => group.Key))
            {
                DatabaseObjectNode schemaNode = new DatabaseObjectNode
                {
                    Name = schemaGroup.Key,
                    Kind = DatabaseObjectKind.Schema,
                    DatabaseName = databaseName
                };

                foreach (dynamic tableRow in schemaGroup)
                {
                    string objectName = tableRow.ObjectName;
                    string objectType = tableRow.ObjectType;
                    DatabaseObjectKind kind = objectType == "VIEW" ? DatabaseObjectKind.View : DatabaseObjectKind.Table;
                    DatabaseObjectNode tableNode = new DatabaseObjectNode
                    {
                        Name = objectName,
                        Kind = kind,
                        DatabaseName = databaseName,
                        SchemaName = schemaGroup.Key
                    };

                    foreach (dynamic columnRow in columns.Where(column => string.Equals((string)column.SchemaName, schemaGroup.Key, StringComparison.OrdinalIgnoreCase) && string.Equals((string)column.TableName, objectName, StringComparison.OrdinalIgnoreCase)))
                    {
                        tableNode.Children.Add(new DatabaseObjectNode
                        {
                            Name = columnRow.ColumnName,
                            Kind = DatabaseObjectKind.Column,
                            DatabaseName = databaseName,
                            SchemaName = schemaGroup.Key,
                            ParentName = objectName,
                            DataType = BuildSqlServerTypeName(columnRow.DataType, columnRow.MaxLength, columnRow.PrecisionValue, columnRow.ScaleValue),
                            Comment = columnRow.CommentText == null ? string.Empty : Convert.ToString(columnRow.CommentText)
                        });
                    }

                    schemaNode.Children.Add(tableNode);
                }

                databaseNode.Children.Add(schemaNode);
            }

            return databaseNode;
        }

        /// <summary>
        /// 当单个数据库读取失败时，返回错误提示节点。
        /// </summary>
        private static DatabaseObjectNode CreateErrorDatabaseNode(string databaseName, string message)
        {
            DatabaseObjectNode databaseNode = new DatabaseObjectNode
            {
                Name = databaseName,
                Kind = DatabaseObjectKind.Database,
                DatabaseName = databaseName
            };
            databaseNode.Children.Add(new DatabaseObjectNode
            {
                Name = message,
                Kind = DatabaseObjectKind.Info
            });
            return databaseNode;
        }

        /// <summary>
        /// 根据 SQL Server 类型长度信息生成可读字段类型。
        /// </summary>
        private static string BuildSqlServerTypeName(string dataType, short maxLength, byte precision, byte scale)
        {
            if (dataType == "nvarchar" || dataType == "nchar")
            {
                return dataType + "(" + (maxLength < 0 ? "MAX" : (maxLength / 2).ToString()) + ")";
            }

            if (dataType == "varchar" || dataType == "char" || dataType == "varbinary")
            {
                return dataType + "(" + (maxLength < 0 ? "MAX" : maxLength.ToString()) + ")";
            }

            if (dataType == "decimal" || dataType == "numeric")
            {
                return dataType + "(" + precision + "," + scale + ")";
            }

            return dataType;
        }

        /// <summary>
        /// 将标识符转成 SQL Server 可直接拼接的方括号形式。
        /// </summary>
        private static string QuoteSqlServerIdentifier(string identifier)
        {
            return "[" + (identifier ?? string.Empty).Replace("]", "]]") + "]";
        }
    }
}
