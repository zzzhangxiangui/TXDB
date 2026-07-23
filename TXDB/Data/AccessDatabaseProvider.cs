using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using TXDB.Models;

namespace TXDB.Data
{
    /// <summary>
    /// Access 数据库操作实现。
    /// </summary>
    public class AccessDatabaseProvider : DatabaseProviderBase
    {
        /// <summary>
        /// 当前 Provider 对应的数据库类型。
        /// </summary>
        public override DatabaseProviderType ProviderType
        {
            get { return DatabaseProviderType.Access; }
        }

        /// <summary>
        /// 当前 Provider 的显示名称。
        /// </summary>
        public override string DisplayName
        {
            get { return "Access"; }
        }

        /// <summary>
        /// 创建 Access OleDb 连接。
        /// </summary>
        public override IDbConnection CreateConnection(ConnectionProfile profile)
        {
            return new OleDbConnection(BuildConnectionString(profile));
        }

        /// <summary>
        /// 根据配置生成 Access 连接字符串。
        /// </summary>
        public override string BuildConnectionString(ConnectionProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.RawConnectionString))
            {
                return profile.RawConnectionString;
            }

            OleDbConnectionStringBuilder builder = new OleDbConnectionStringBuilder();
            builder.Provider = "Microsoft.ACE.OLEDB.12.0";
            builder.DataSource = profile.FilePath;
            if (!string.IsNullOrWhiteSpace(profile.Password))
            {
                builder["Jet OLEDB:Database Password"] = profile.Password;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// 读取 Access 数据库列表，文件型数据库固定显示当前文件。
        /// </summary>
        public override IList<DatabaseObjectNode> LoadMetadata(ConnectionProfile profile)
        {
            TestConnection(profile);
            string databaseName = Path.GetFileName(profile.FilePath);
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
        /// 按需读取 Access 表和字段元数据。
        /// </summary>
        public override DatabaseObjectNode LoadDatabaseMetadata(ConnectionProfile profile, string databaseName)
        {
            using (OleDbConnection connection = new OleDbConnection(BuildConnectionString(profile)))
            {
                connection.Open();
                DataTable tableSchema = connection.GetSchema("Tables");
                DataTable columnSchema = connection.GetSchema("Columns");
                string safeDatabaseName = string.IsNullOrWhiteSpace(databaseName) ? Path.GetFileName(profile.FilePath) : databaseName;

                DatabaseObjectNode databaseNode = new DatabaseObjectNode
                {
                    Name = safeDatabaseName,
                    Kind = DatabaseObjectKind.Database,
                    DatabaseName = safeDatabaseName
                };

                foreach (DataRow tableRow in tableSchema.Rows)
                {
                    string tableName = tableRow["TABLE_NAME"].ToString();
                    string tableType = tableRow["TABLE_TYPE"].ToString();
                    if (tableName.StartsWith("MSys"))
                    {
                        continue;
                    }

                    DatabaseObjectNode tableNode = new DatabaseObjectNode
                    {
                        Name = tableName,
                        Kind = tableType == "VIEW" ? DatabaseObjectKind.View : DatabaseObjectKind.Table,
                        DatabaseName = databaseNode.Name
                    };

                    foreach (DataRow columnRow in columnSchema.Select("TABLE_NAME = '" + tableName.Replace("'", "''") + "'"))
                    {
                        tableNode.Children.Add(new DatabaseObjectNode
                        {
                            Name = columnRow["COLUMN_NAME"].ToString(),
                            Kind = DatabaseObjectKind.Column,
                            DatabaseName = databaseNode.Name,
                            ParentName = tableName,
                            DataType = columnRow["DATA_TYPE"].ToString()
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
        /// 读取 Access 表字段结构。
        /// </summary>
        public override DataTable LoadTableSchema(ConnectionProfile profile, DatabaseObjectNode tableNode)
        {
            DataTable schemaTable = CreateSchemaTable();
            if (tableNode == null)
            {
                return schemaTable;
            }

            using (OleDbConnection connection = new OleDbConnection(BuildConnectionString(profile)))
            {
                connection.Open();
                DataTable columnSchema = connection.GetSchema("Columns", new[] { null, null, tableNode.Name, null });
                HashSet<string> primaryKeys = LoadPrimaryKeys(connection, tableNode.Name);
                int index = 1;
                foreach (DataRow columnRow in columnSchema.Rows)
                {
                    DataRow row = schemaTable.NewRow();
                    row["序号"] = index++;
                    row["字段名"] = columnRow["COLUMN_NAME"].ToString();
                    row["主键"] = primaryKeys.Contains(columnRow["COLUMN_NAME"].ToString()) ? "是" : string.Empty;
                    row["类型"] = columnRow["DATA_TYPE"].ToString();
                    row["可空"] = columnRow.Table.Columns.Contains("IS_NULLABLE") ? columnRow["IS_NULLABLE"].ToString() : string.Empty;
                    row["默认值"] = columnRow.Table.Columns.Contains("COLUMN_DEFAULT") ? columnRow["COLUMN_DEFAULT"].ToString() : string.Empty;
                    row["注释"] = string.Empty;
                    schemaTable.Rows.Add(row);
                }
            }

            return schemaTable;
        }

        /// <summary>
        /// 使用 OleDbDataAdapter 按起始记录读取 Access 表预览页。
        /// </summary>
        public override QueryExecutionResult PreviewTable(ConnectionProfile profile, DatabaseObjectNode tableNode, int pageIndex, int pageSize)
        {
            if (profile == null)
            {
                throw new InvalidOperationException("请先选择数据库连接。");
            }

            if (tableNode == null || string.IsNullOrWhiteSpace(tableNode.Name))
            {
                throw new InvalidOperationException("请选择需要预览的表或视图。");
            }

            int safePageIndex = System.Math.Max(1, pageIndex);
            int safePageSize = System.Math.Max(1, pageSize);
            int offset = System.Math.Max(0, (safePageIndex - 1) * safePageSize);
            Stopwatch stopwatch = Stopwatch.StartNew();
            QueryExecutionResult result = new QueryExecutionResult();

            string previewSql = safePageIndex <= 1
                ? "SELECT TOP " + safePageSize + " * FROM " + QuoteAccessName(tableNode.Name)
                : "SELECT * FROM " + QuoteAccessName(tableNode.Name);

            using (OleDbConnection connection = new OleDbConnection(BuildConnectionString(profile)))
            {
                connection.Open();
                using (OleDbCommand countCommand = new OleDbCommand("SELECT COUNT(*) FROM " + QuoteAccessName(tableNode.Name), connection))
                {
                    object countValue = countCommand.ExecuteScalar();
                    result.TotalRows = countValue == null || countValue == DBNull.Value ? 0 : Convert.ToInt32(countValue);
                }

                using (OleDbDataAdapter adapter = new OleDbDataAdapter(previewSql, connection))
                {
                    DataSet dataSet = new DataSet();
                    adapter.Fill(dataSet, safePageIndex <= 1 ? 0 : offset, safePageSize, "Result");
                    if (dataSet.Tables.Count > 0)
                    {
                        result.Table = dataSet.Tables[0];
                    }
                }
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Message = "已加载第 " + safePageIndex + " 页，返回 " + (result.Table == null ? 0 : result.Table.Rows.Count) + " 行，共 " + result.TotalRows + " 行。";
            return result;
        }

        /// <summary>
        /// 读取 Access 表的主键列名称集合。
        /// </summary>
        private static HashSet<string> LoadPrimaryKeys(OleDbConnection connection, string tableName)
        {
            HashSet<string> primaryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                DataTable primaryKeyTable = connection.GetSchema("Primary_Keys", new[] { null, null, tableName });
                foreach (DataRow row in primaryKeyTable.Rows)
                {
                    string columnName = row["COLUMN_NAME"].ToString();
                    if (!string.IsNullOrWhiteSpace(columnName))
                    {
                        primaryKeys.Add(columnName);
                    }
                }
            }
            catch
            {
                // Access 的主键 schema 在不同驱动环境下可能不可用，拿不到时就保持空集合。
            }

            return primaryKeys;
        }

        /// <summary>
        /// 转义 Access 方括号标识符。
        /// </summary>
        private static string QuoteAccessName(string name)
        {
            return "[" + (name ?? string.Empty).Replace("]", "]]") + "]";
        }
    }
}
