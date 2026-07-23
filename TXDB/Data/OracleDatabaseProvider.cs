using System.Collections.Generic;
using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using TXDB.Models;

namespace TXDB.Data
{
    /// <summary>
    /// Oracle 数据库操作实现。
    /// </summary>
    public class OracleDatabaseProvider : DatabaseProviderBase
    {
        /// <summary>
        /// 当前 Provider 对应的数据库类型。
        /// </summary>
        public override DatabaseProviderType ProviderType
        {
            get { return DatabaseProviderType.Oracle; }
        }

        /// <summary>
        /// 当前 Provider 的显示名称。
        /// </summary>
        public override string DisplayName
        {
            get { return "Oracle"; }
        }

        /// <summary>
        /// 创建 Oracle 连接。
        /// </summary>
        public override IDbConnection CreateConnection(ConnectionProfile profile)
        {
            return new OracleConnection(BuildConnectionString(profile));
        }

        /// <summary>
        /// 根据配置生成 Oracle 连接字符串。
        /// </summary>
        public override string BuildConnectionString(ConnectionProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.RawConnectionString))
            {
                return profile.RawConnectionString;
            }

            OracleConnectionStringBuilder builder = new OracleConnectionStringBuilder();
            builder.DataSource = string.IsNullOrWhiteSpace(profile.Port)
                ? profile.Host + "/" + profile.DatabaseName
                : profile.Host + ":" + profile.Port + "/" + profile.DatabaseName;
            builder.UserID = profile.UserName;
            builder.Password = profile.Password;
            return builder.ConnectionString;
        }

        /// <summary>
        /// 读取 Oracle 数据库列表，当前阶段固定展示当前服务名。
        /// </summary>
        public override IList<DatabaseObjectNode> LoadMetadata(ConnectionProfile profile)
        {
            TestConnection(profile);
            string databaseName = string.IsNullOrWhiteSpace(profile.DatabaseName) ? profile.UserName : profile.DatabaseName;
            return new List<DatabaseObjectNode>
            {
                new DatabaseObjectNode
                {
                    Name = databaseName,
                    Kind = DatabaseObjectKind.Database,
                    DatabaseName = databaseName
                }
            };
        }

        /// <summary>
        /// 按需读取 Oracle 表、视图、字段和注释元数据。
        /// </summary>
        public override DatabaseObjectNode LoadDatabaseMetadata(ConnectionProfile profile, string databaseName)
        {
            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                IEnumerable<dynamic> objects = connection.Query(@"
SELECT OWNER AS OwnerName, TABLE_NAME AS ObjectName, 'TABLE' AS ObjectType
FROM ALL_TABLES
WHERE OWNER = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')
UNION ALL
SELECT OWNER AS OwnerName, VIEW_NAME AS ObjectName, 'VIEW' AS ObjectType
FROM ALL_VIEWS
WHERE OWNER = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')
ORDER BY OwnerName, ObjectName");

                DatabaseObjectNode databaseNode = new DatabaseObjectNode
                {
                    Name = string.IsNullOrWhiteSpace(databaseName) ? (string.IsNullOrWhiteSpace(profile.DatabaseName) ? profile.UserName : profile.DatabaseName) : databaseName,
                    Kind = DatabaseObjectKind.Database,
                    DatabaseName = string.IsNullOrWhiteSpace(databaseName) ? (string.IsNullOrWhiteSpace(profile.DatabaseName) ? profile.UserName : profile.DatabaseName) : databaseName
                };

                foreach (dynamic row in objects)
                {
                    string ownerName = ReadDynamic(row, "OWNERNAME");
                    string objectName = ReadDynamic(row, "OBJECTNAME");
                    string objectType = ReadDynamic(row, "OBJECTTYPE");
                    DatabaseObjectNode tableNode = new DatabaseObjectNode
                    {
                        Name = objectName,
                        Kind = objectType == "VIEW" ? DatabaseObjectKind.View : DatabaseObjectKind.Table,
                        DatabaseName = databaseNode.Name,
                        SchemaName = ownerName
                    };

                    foreach (dynamic column in connection.Query(@"
SELECT c.COLUMN_NAME AS ColumnName, c.DATA_TYPE AS DataType, cc.COMMENTS AS CommentText
FROM ALL_TAB_COLUMNS c
LEFT JOIN ALL_COL_COMMENTS cc ON cc.OWNER = c.OWNER AND cc.TABLE_NAME = c.TABLE_NAME AND cc.COLUMN_NAME = c.COLUMN_NAME
WHERE c.OWNER = :OwnerName AND c.TABLE_NAME = :ObjectName
ORDER BY c.COLUMN_ID", new { OwnerName = ownerName, ObjectName = objectName }))
                    {
                        tableNode.Children.Add(new DatabaseObjectNode
                        {
                            Name = ReadDynamic(column, "COLUMNNAME"),
                            Kind = DatabaseObjectKind.Column,
                            DatabaseName = databaseNode.Name,
                            SchemaName = ownerName,
                            ParentName = objectName,
                            DataType = ReadDynamic(column, "DATATYPE"),
                            Comment = ReadDynamic(column, "COMMENTTEXT")
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
        /// 读取 Oracle 字段结构和字段注释。
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
                IEnumerable<dynamic> rows = connection.Query(@"
SELECT
    c.COLUMN_ID AS Ordinal,
    c.COLUMN_NAME AS ColumnName,
    CASE WHEN pk.COLUMN_NAME IS NULL THEN 'N' ELSE 'Y' END AS IsPrimaryKey,
    c.DATA_TYPE AS DataType,
    c.DATA_LENGTH AS DataLength,
    c.DATA_PRECISION AS PrecisionValue,
    c.DATA_SCALE AS ScaleValue,
    c.NULLABLE AS NullableValue,
    c.DATA_DEFAULT AS DefaultValue,
    cc.COMMENTS AS CommentText
FROM ALL_TAB_COLUMNS c
LEFT JOIN ALL_COL_COMMENTS cc ON cc.OWNER = c.OWNER AND cc.TABLE_NAME = c.TABLE_NAME AND cc.COLUMN_NAME = c.COLUMN_NAME
LEFT JOIN (
    SELECT acc.OWNER, acc.TABLE_NAME, acc.COLUMN_NAME
    FROM ALL_CONSTRAINTS ac
    INNER JOIN ALL_CONS_COLUMNS acc ON ac.OWNER = acc.OWNER AND ac.CONSTRAINT_NAME = acc.CONSTRAINT_NAME
    WHERE ac.CONSTRAINT_TYPE = 'P'
) pk ON pk.OWNER = c.OWNER AND pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
WHERE c.OWNER = :OwnerName AND c.TABLE_NAME = :ObjectName
ORDER BY c.COLUMN_ID", new { OwnerName = tableNode.SchemaName, ObjectName = tableNode.Name });

                foreach (dynamic row in rows)
                {
                    DataRow dataRow = schemaTable.NewRow();
                    dataRow["序号"] = ReadDynamic(row, "ORDINAL");
                    dataRow["字段名"] = ReadDynamic(row, "COLUMNNAME");
                    dataRow["主键"] = ReadDynamic(row, "ISPRIMARYKEY") == "Y" ? "是" : string.Empty;
                    dataRow["类型"] = BuildOracleTypeName(row);
                    dataRow["可空"] = ReadDynamic(row, "NULLABLEVALUE") == "Y" ? "是" : "否";
                    dataRow["默认值"] = ReadDynamic(row, "DEFAULTVALUE");
                    dataRow["注释"] = ReadDynamic(row, "COMMENTTEXT");
                    schemaTable.Rows.Add(dataRow);
                }
            }

            return schemaTable;
        }

        /// <summary>
        /// 根据 Oracle 字段元数据生成可读类型名称。
        /// </summary>
        private static string BuildOracleTypeName(dynamic row)
        {
            string dataType = ReadDynamic(row, "DATATYPE");
            string precision = ReadDynamic(row, "PRECISIONVALUE");
            string scale = ReadDynamic(row, "SCALEVALUE");
            string length = ReadDynamic(row, "DATALENGTH");

            if ((dataType == "NUMBER" || dataType == "FLOAT") && !string.IsNullOrWhiteSpace(precision))
            {
                return string.IsNullOrWhiteSpace(scale) ? dataType + "(" + precision + ")" : dataType + "(" + precision + "," + scale + ")";
            }

            if ((dataType == "VARCHAR2" || dataType == "NVARCHAR2" || dataType == "CHAR" || dataType == "NCHAR") && !string.IsNullOrWhiteSpace(length))
            {
                return dataType + "(" + length + ")";
            }

            return dataType;
        }

        /// <summary>
        /// 从 Dapper 动态行中按忽略大小写方式读取列值。
        /// </summary>
        private static string ReadDynamic(dynamic row, string columnName)
        {
            IDictionary<string, object> values = row as IDictionary<string, object>;
            if (values == null)
            {
                return string.Empty;
            }

            foreach (KeyValuePair<string, object> pair in values)
            {
                if (string.Equals(pair.Key, columnName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value == null ? string.Empty : pair.Value.ToString();
                }
            }

            return string.Empty;
        }
    }
}
