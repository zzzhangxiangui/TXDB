using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Dapper;
using TXDB.Models;

namespace TXDB.Data
{
    /// <summary>
    /// 通过 ADO.NET Provider Factory 接入 SQLite、MySQL、Oracle 等外部驱动。
    /// </summary>
    public class FactoryDatabaseProvider : DatabaseProviderBase
    {
        private readonly DatabaseProviderType providerType;
        private readonly string displayName;
        private readonly string invariantName;

        /// <summary>
        /// 初始化工厂型数据库 Provider。
        /// </summary>
        public FactoryDatabaseProvider(DatabaseProviderType providerType, string displayName, string invariantName)
        {
            this.providerType = providerType;
            this.displayName = displayName;
            this.invariantName = invariantName;
        }

        /// <summary>
        /// 当前 Provider 对应的数据库类型。
        /// </summary>
        public override DatabaseProviderType ProviderType
        {
            get { return providerType; }
        }

        /// <summary>
        /// 当前 Provider 的显示名称。
        /// </summary>
        public override string DisplayName
        {
            get { return displayName; }
        }

        /// <summary>
        /// 根据 ADO.NET Provider Factory 创建连接。
        /// </summary>
        public override IDbConnection CreateConnection(ConnectionProfile profile)
        {
            DbProviderFactory factory = CreateFactory();
            DbConnection connection = factory.CreateConnection();
            if (connection == null)
            {
                throw new InvalidOperationException(displayName + " 驱动未能创建连接对象。");
            }

            connection.ConnectionString = BuildConnectionString(profile);
            return connection;
        }

        /// <summary>
        /// 根据数据库类型生成基础连接字符串。
        /// </summary>
        public override string BuildConnectionString(ConnectionProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.RawConnectionString))
            {
                return profile.RawConnectionString;
            }

            if (providerType == DatabaseProviderType.SQLite)
            {
                return "Data Source=" + profile.FilePath + ";Version=3;";
            }

            if (providerType == DatabaseProviderType.MySQL)
            {
                string port = string.IsNullOrWhiteSpace(profile.Port) ? "3306" : profile.Port;
                return "Server=" + profile.Host + ";Port=" + port + ";Database=" + profile.DatabaseName + ";Uid=" + profile.UserName + ";Pwd=" + profile.Password + ";CharSet=utf8;";
            }

            if (providerType == DatabaseProviderType.Oracle)
            {
                string dataSource = string.IsNullOrWhiteSpace(profile.Port)
                    ? profile.Host + "/" + profile.DatabaseName
                    : profile.Host + ":" + profile.Port + "/" + profile.DatabaseName;
                return "Data Source=" + dataSource + ";User Id=" + profile.UserName + ";Password=" + profile.Password + ";";
            }

            return profile.RawConnectionString;
        }

        /// <summary>
        /// 根据不同数据库类型读取数据库列表；首阶段 MySQL 先只列出库名。
        /// </summary>
        public override IList<DatabaseObjectNode> LoadMetadata(ConnectionProfile profile)
        {
            if (providerType == DatabaseProviderType.MySQL)
            {
                return LoadMySqlDatabaseList(profile);
            }

            TestConnection(profile);
            return new List<DatabaseObjectNode>
            {
                CreateInfoNode(displayName + " 元数据方言待补齐，可以先执行 SQL 查询。")
            };
        }

        /// <summary>
        /// 按需读取指定数据库下的对象元数据。
        /// </summary>
        public override DatabaseObjectNode LoadDatabaseMetadata(ConnectionProfile profile, string databaseName)
        {
            if (providerType == DatabaseProviderType.MySQL)
            {
                return LoadSingleMySqlDatabaseMetadata(profile, databaseName);
            }

            return base.LoadDatabaseMetadata(profile, databaseName);
        }

        /// <summary>
        /// 读取 MySQL 数据库、表和字段元数据。
        /// </summary>
        public override DataTable LoadTableSchema(ConnectionProfile profile, DatabaseObjectNode tableNode)
        {
            if (providerType != DatabaseProviderType.MySQL)
            {
                return base.LoadTableSchema(profile, tableNode);
            }

            DataTable schemaTable = CreateSchemaTable();
            if (tableNode == null)
            {
                return schemaTable;
            }

            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                string schemaName = string.IsNullOrWhiteSpace(tableNode.SchemaName) ? tableNode.DatabaseName : tableNode.SchemaName;
                if (string.IsNullOrWhiteSpace(schemaName))
                {
                    return schemaTable;
                }

                IEnumerable<dynamic> rows = connection.Query(@"
SELECT
    c.ORDINAL_POSITION AS Ordinal,
    c.COLUMN_NAME AS ColumnName,
    CASE WHEN pk.COLUMN_NAME IS NULL THEN 'N' ELSE 'Y' END AS IsPrimaryKey,
    c.DATA_TYPE AS DataType,
    c.CHARACTER_MAXIMUM_LENGTH AS CharLength,
    c.NUMERIC_PRECISION AS PrecisionValue,
    c.NUMERIC_SCALE AS ScaleValue,
    c.IS_NULLABLE AS NullableValue,
    c.COLUMN_DEFAULT AS DefaultValue,
    c.COLUMN_COMMENT AS CommentText
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN (
    SELECT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
    INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA AND tc.TABLE_NAME = kcu.TABLE_NAME
    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
) pk ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA AND pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
WHERE c.TABLE_SCHEMA = @SchemaName AND c.TABLE_NAME = @ObjectName
ORDER BY c.ORDINAL_POSITION", new { SchemaName = schemaName, ObjectName = tableNode.Name });

                foreach (dynamic row in rows)
                {
                    DataRow dataRow = schemaTable.NewRow();
                    dataRow["序号"] = row.Ordinal;
                    dataRow["字段名"] = row.ColumnName;
                    dataRow["主键"] = ReadMySqlText(row, "ISPRIMARYKEY") == "Y" ? "是" : string.Empty;
                    dataRow["类型"] = BuildMySqlTypeName(row);
                    dataRow["可空"] = ReadMySqlText(row, "NULLABLEVALUE") == "YES" ? "是" : "否";
                    dataRow["默认值"] = ReadMySqlText(row, "DEFAULTVALUE");
                    dataRow["注释"] = ReadMySqlText(row, "COMMENTTEXT");
                    schemaTable.Rows.Add(dataRow);
                }
            }

            return schemaTable;
        }

        /// <summary>
        /// 读取 MySQL 数据库列表。
        /// </summary>
        private IList<DatabaseObjectNode> LoadMySqlDatabaseList(ConnectionProfile profile)
        {
            TestConnection(profile);
            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                List<string> databaseNames = connection.Query<string>(@"
SELECT SCHEMA_NAME
FROM INFORMATION_SCHEMA.SCHEMATA
WHERE SCHEMA_NAME NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
ORDER BY SCHEMA_NAME").ToList();

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
        /// 按需读取单个 MySQL 数据库下的表、视图和字段元数据。
        /// </summary>
        private DatabaseObjectNode LoadSingleMySqlDatabaseMetadata(ConnectionProfile profile, string databaseName)
        {
            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                List<dynamic> tables = connection.Query(@"
SELECT
    TABLE_SCHEMA AS DatabaseName,
    TABLE_NAME AS ObjectName,
    TABLE_TYPE AS ObjectType
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = @DatabaseName
  AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')
ORDER BY TABLE_NAME", new { DatabaseName = databaseName }).ToList();

                List<dynamic> columns = connection.Query(@"
SELECT
    TABLE_SCHEMA AS DatabaseName,
    TABLE_NAME AS TableName,
    COLUMN_NAME AS ColumnName,
    DATA_TYPE AS DataType,
    COLUMN_COMMENT AS CommentText
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @DatabaseName
ORDER BY TABLE_NAME, ORDINAL_POSITION", new { DatabaseName = databaseName }).ToList();

                return BuildMySqlDatabaseNode(databaseName, tables, columns);
            }
        }

        /// <summary>
        /// 将 MySQL 元数据整理成数据库树。
        /// </summary>
        private static DatabaseObjectNode BuildMySqlDatabaseNode(string databaseName, List<dynamic> tables, List<dynamic> columns)
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
                string objectName = tableRow.ObjectName;
                string objectType = tableRow.ObjectType;
                DatabaseObjectKind kind = objectType == "VIEW" ? DatabaseObjectKind.View : DatabaseObjectKind.Table;
                DatabaseObjectNode tableNode = new DatabaseObjectNode
                {
                    Name = objectName,
                    Kind = kind,
                    DatabaseName = databaseName,
                    SchemaName = databaseName
                };

                foreach (dynamic columnRow in columns.Where(column => string.Equals((string)column.TableName, objectName, StringComparison.OrdinalIgnoreCase)))
                {
                    tableNode.Children.Add(new DatabaseObjectNode
                        {
                            Name = columnRow.ColumnName,
                            Kind = DatabaseObjectKind.Column,
                            DatabaseName = databaseName,
                            SchemaName = databaseName,
                            ParentName = objectName,
                            DataType = ReadMySqlText(columnRow, "DATATYPE"),
                            Comment = ReadMySqlText(columnRow, "COMMENTTEXT")
                        });
                }

                databaseNode.Children.Add(tableNode);
            }

            return databaseNode;
        }

        /// <summary>
        /// 根据 MySQL 字段元数据生成可读类型名称。
        /// </summary>
        private static string BuildMySqlTypeName(dynamic row)
        {
            string dataType = ReadMySqlText(row, "DATATYPE");
            string charLength = ReadMySqlText(row, "CHARLENGTH");
            string precision = ReadMySqlText(row, "PRECISIONVALUE");
            string scale = ReadMySqlText(row, "SCALEVALUE");

            if ((dataType == "varchar" || dataType == "char" || dataType == "varbinary" || dataType == "text") && !string.IsNullOrWhiteSpace(charLength))
            {
                return dataType + "(" + charLength + ")";
            }

            if ((dataType == "decimal" || dataType == "numeric" || dataType == "double" || dataType == "float") && !string.IsNullOrWhiteSpace(precision))
            {
                return string.IsNullOrWhiteSpace(scale) ? dataType + "(" + precision + ")" : dataType + "(" + precision + "," + scale + ")";
            }

            return dataType;
        }

        /// <summary>
        /// 从 MySQL 动态结果中读取文本值。
        /// </summary>
        private static string ReadMySqlText(dynamic row, string columnName)
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
        /// 创建对应的 ADO.NET Provider Factory。
        /// </summary>
        private DbProviderFactory CreateFactory()
        {
            try
            {
                return DbProviderFactories.GetFactory(invariantName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("未找到 " + displayName + " 驱动，请确认对应 Provider 已安装或已注册：" + invariantName, ex);
            }
        }
    }
}
