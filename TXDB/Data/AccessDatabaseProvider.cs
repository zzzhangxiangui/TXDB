using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using System.Text;
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
            if (profile == null)
            {
                throw new InvalidOperationException("Access 连接配置不能为空。");
            }

            if (!string.IsNullOrWhiteSpace(profile.RawConnectionString))
            {
                return profile.RawConnectionString;
            }

            OleDbConnectionStringBuilder builder = new OleDbConnectionStringBuilder();
            builder.Provider = ResolveAccessProvider(profile.FilePath);
            builder.DataSource = profile.FilePath;
            if (!string.IsNullOrWhiteSpace(profile.Password))
            {
                builder["Jet OLEDB:Database Password"] = profile.Password;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// 测试 Access 连接，并把 OleDb 驱动缺失错误转换为更容易理解的中文提示。
        /// </summary>
        public override void TestConnection(ConnectionProfile profile)
        {
            using (OleDbConnection connection = OpenAccessConnection(profile))
            {
            }
        }

        /// <summary>
        /// 执行 Access SQL，并统一处理 OleDb Provider 未安装或位数不匹配的提示。
        /// </summary>
        public override QueryExecutionResult Execute(ConnectionProfile profile, string sql)
        {
            try
            {
                return base.Execute(profile, sql);
            }
            catch (Exception ex)
            {
                ThrowFriendlyProviderException(ex, profile);
                throw;
            }
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
            using (OleDbConnection connection = OpenAccessConnection(profile))
            {
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

            using (OleDbConnection connection = OpenAccessConnection(profile))
            {
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
            return PreviewTable(profile, tableNode, pageIndex, pageSize, null);
        }

        /// <summary>
        /// 使用 OleDbDataAdapter 按起始记录读取带快速筛选条件的 Access 表预览页。
        /// </summary>
        public override QueryExecutionResult PreviewTable(ConnectionProfile profile, DatabaseObjectNode tableNode, int pageIndex, int pageSize, IList<TableFilterCondition> filters)
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
            TableFilterSqlBuildResult filterSql = BuildTableFilterWhereClause(tableNode, filters, true);
            string tableName = QuoteAccessName(tableNode.Name);

            string previewSql = safePageIndex <= 1
                ? "SELECT TOP " + safePageSize + " * FROM " + tableName + NormalizeWhereClause(filterSql.WhereClause)
                : "SELECT * FROM " + tableName + NormalizeWhereClause(filterSql.WhereClause);

            using (OleDbConnection connection = OpenAccessConnection(profile))
            {
                using (OleDbCommand countCommand = new OleDbCommand("SELECT COUNT(*) FROM " + tableName + NormalizeWhereClause(filterSql.WhereClause), connection))
                {
                    AddOleDbParameters(countCommand, filterSql.Parameters);
                    object countValue = countCommand.ExecuteScalar();
                    result.TotalRows = countValue == null || countValue == DBNull.Value ? 0 : Convert.ToInt32(countValue);
                }

                using (OleDbCommand previewCommand = new OleDbCommand(previewSql, connection))
                {
                    AddOleDbParameters(previewCommand, filterSql.Parameters);
                    using (OleDbDataAdapter adapter = new OleDbDataAdapter(previewCommand))
                    {
                        DataSet dataSet = new DataSet();
                        adapter.Fill(dataSet, safePageIndex <= 1 ? 0 : offset, safePageSize, "Result");
                        if (dataSet.Tables.Count > 0)
                        {
                            result.Table = dataSet.Tables[0];
                        }
                    }
                }
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Message = "已加载第 " + safePageIndex + " 页，返回 " + (result.Table == null ? 0 : result.Table.Rows.Count) + " 行，共 " + result.TotalRows + " 行。";
            return result;
        }

        /// <summary>
        /// 按顺序添加 OleDb 位置参数，Access 查询中的问号占位符依赖添加顺序。
        /// </summary>
        private static void AddOleDbParameters(OleDbCommand command, IList<TableFilterParameter> parameters)
        {
            if (command == null || parameters == null)
            {
                return;
            }

            foreach (TableFilterParameter parameter in parameters)
            {
                OleDbParameter oleDbParameter = command.CreateParameter();
                oleDbParameter.ParameterName = parameter.Name;
                oleDbParameter.Value = parameter.Value ?? DBNull.Value;
                command.Parameters.Add(oleDbParameter);
            }
        }

        /// <summary>
        /// 创建并打开 Access 连接，便于所有 Access 操作复用同一套错误提示。
        /// </summary>
        private OleDbConnection OpenAccessConnection(ConnectionProfile profile)
        {
            OleDbConnection connection = new OleDbConnection(BuildConnectionString(profile));
            try
            {
                connection.Open();
                return connection;
            }
            catch (Exception ex)
            {
                connection.Dispose();
                ThrowFriendlyProviderException(ex, profile);
                throw;
            }
        }

        /// <summary>
        /// 根据文件类型和当前进程可见的 OleDb Provider 选择 Access 驱动。
        /// </summary>
        private static string ResolveAccessProvider(string filePath)
        {
            IList<string> candidates = BuildProviderCandidates(filePath);
            HashSet<string> installedProviders = LoadInstalledOleDbProviders();
            foreach (string candidate in candidates)
            {
                if (installedProviders.Contains(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(BuildMissingProviderMessage(filePath, candidates));
        }

        /// <summary>
        /// 根据 Access 文件扩展名生成候选 Provider，老的 .mdb 可兼容 Jet 4.0。
        /// </summary>
        private static IList<string> BuildProviderCandidates(string filePath)
        {
            List<string> candidates = new List<string>();
            candidates.Add("Microsoft.ACE.OLEDB.16.0");
            candidates.Add("Microsoft.ACE.OLEDB.12.0");

            string extension = Path.GetExtension(filePath ?? string.Empty);
            if (string.Equals(extension, ".mdb", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("Microsoft.Jet.OLEDB.4.0");
            }

            return candidates;
        }

        /// <summary>
        /// 读取当前进程位数下已经注册的 OleDb Provider 名称。
        /// </summary>
        private static HashSet<string> LoadInstalledOleDbProviders()
        {
            HashSet<string> providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (OleDbDataReader reader = OleDbEnumerator.GetRootEnumerator())
                {
                    if (reader == null)
                    {
                        return providers;
                    }

                    while (reader.Read())
                    {
                        string providerName = reader["SOURCES_NAME"] == null ? string.Empty : reader["SOURCES_NAME"].ToString();
                        if (!string.IsNullOrWhiteSpace(providerName))
                        {
                            providers.Add(providerName);
                        }
                    }
                }
            }
            catch
            {
                // 部分精简系统读取 Provider 列表可能失败，失败时让后续连接错误给出统一提示。
            }

            return providers;
        }

        /// <summary>
        /// 将 OleDb 未注册错误转换为明确的 Access 驱动安装或位数提示。
        /// </summary>
        private static void ThrowFriendlyProviderException(Exception ex, ConnectionProfile profile)
        {
            if (IsProviderMissingException(ex))
            {
                throw new InvalidOperationException(BuildMissingProviderMessage(profile == null ? string.Empty : profile.FilePath, BuildProviderCandidates(profile == null ? string.Empty : profile.FilePath)), ex);
            }
        }

        /// <summary>
        /// 判断异常是否来自 OleDb Provider 未安装、未注册或位数不匹配。
        /// </summary>
        private static bool IsProviderMissingException(Exception ex)
        {
            Exception current = ex;
            while (current != null)
            {
                string message = current.Message ?? string.Empty;
                if (message.IndexOf("未在本地计算机上注册", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("not registered", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("未找到提供程序", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        /// <summary>
        /// 构造 Access Provider 缺失时显示给用户的中文说明。
        /// </summary>
        private static string BuildMissingProviderMessage(string filePath, IList<string> candidates)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("当前电脑没有注册本程序可用的 Access 数据库驱动。");
            builder.AppendLine();
            builder.Append("当前程序进程位数：");
            builder.Append(Environment.Is64BitProcess ? "64 位" : "32 位");
            builder.Append("；操作系统位数：");
            builder.Append(Environment.Is64BitOperatingSystem ? "64 位" : "32 位");
            builder.Append("。");
            builder.AppendLine();
            builder.Append("需要安装与当前程序位数一致的 Microsoft Access Database Engine，或使用已安装 Provider 的完整连接字符串。");
            builder.AppendLine();
            builder.Append("本次可用候选 Provider：");
            builder.Append(string.Join("、", new List<string>(candidates ?? BuildProviderCandidates(filePath)).ToArray()));

            string extension = Path.GetExtension(filePath ?? string.Empty);
            if (string.Equals(extension, ".mdb", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine();
                builder.Append(".mdb 文件也可使用 Microsoft.Jet.OLEDB.4.0，但 Jet 通常只在 32 位进程中可用。");
            }
            else
            {
                builder.AppendLine();
                builder.Append(".accdb 文件必须使用 ACE Provider，例如 Microsoft.ACE.OLEDB.16.0 或 Microsoft.ACE.OLEDB.12.0。");
            }

            return builder.ToString();
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
