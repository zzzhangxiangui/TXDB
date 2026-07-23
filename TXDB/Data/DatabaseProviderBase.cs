using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using Dapper;
using TXDB.Models;

namespace TXDB.Data
{
    /// <summary>
    /// 为各数据库 Provider 提供通用连接测试和 SQL 执行逻辑。
    /// </summary>
    public abstract class DatabaseProviderBase : IDatabaseProvider, IDatabaseMetadataProvider
    {
        /// <summary>
        /// 当前 Provider 对应的数据库类型。
        /// </summary>
        public abstract DatabaseProviderType ProviderType { get; }

        /// <summary>
        /// 当前 Provider 的显示名称。
        /// </summary>
        public abstract string DisplayName { get; }

        /// <summary>
        /// 根据连接配置创建数据库连接。
        /// </summary>
        public abstract IDbConnection CreateConnection(ConnectionProfile profile);

        /// <summary>
        /// 根据连接配置生成连接字符串。
        /// </summary>
        public abstract string BuildConnectionString(ConnectionProfile profile);

        /// <summary>
        /// 读取数据库对象树元数据。
        /// </summary>
        public abstract IList<DatabaseObjectNode> LoadMetadata(ConnectionProfile profile);

        /// <summary>
        /// 默认按数据库名称从元数据根节点中查找数据库详情，具体驱动可覆盖为按需加载。
        /// </summary>
        public virtual DatabaseObjectNode LoadDatabaseMetadata(ConnectionProfile profile, string databaseName)
        {
            foreach (DatabaseObjectNode node in LoadMetadata(profile))
            {
                if (node.Kind == DatabaseObjectKind.Database && SameName(node.Name, databaseName))
                {
                    return node;
                }
            }

            return new DatabaseObjectNode
            {
                Name = databaseName ?? string.Empty,
                Kind = DatabaseObjectKind.Database,
                DatabaseName = databaseName ?? string.Empty
            };
        }

        /// <summary>
        /// 默认从已加载的节点子级中整理表结构信息。
        /// </summary>
        public virtual DataTable LoadTableSchema(ConnectionProfile profile, DatabaseObjectNode tableNode)
        {
            DataTable table = CreateSchemaTable();
            if (tableNode == null)
            {
                return table;
            }

            int index = 1;
            foreach (DatabaseObjectNode columnNode in tableNode.Children.Where(item => item.Kind == DatabaseObjectKind.Column))
            {
                DataRow row = table.NewRow();
                row["序号"] = index++;
                row["字段名"] = columnNode.Name;
                row["主键"] = string.Empty;
                row["类型"] = columnNode.DataType;
                row["可空"] = string.Empty;
                row["默认值"] = string.Empty;
                row["注释"] = columnNode.Comment ?? string.Empty;
                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>
        /// 测试连接是否可以正常打开。
        /// </summary>
        public virtual void TestConnection(ConnectionProfile profile)
        {
            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
            }
        }

        /// <summary>
        /// 使用 Dapper 执行 SQL，并把动态行转换成 DataTable。
        /// </summary>
        public virtual QueryExecutionResult Execute(ConnectionProfile profile, string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new InvalidOperationException("请输入需要执行的 SQL。");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            QueryExecutionResult result = new QueryExecutionResult();
            using (IDbConnection connection = CreateConnection(profile))
            {
                connection.Open();
                if (IsQuerySql(sql))
                {
                    List<dynamic> rows = connection.Query(sql).ToList();
                    result.Table = ConvertRowsToDataTable(rows);
                    result.Message = "查询完成，返回 " + rows.Count + " 行。";
                }
                else
                {
                    result.AffectedRows = connection.Execute(sql);
                    result.Message = "执行完成，影响 " + result.AffectedRows + " 行。";
                }
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            return result;
        }

        /// <summary>
        /// 读取表或视图的某一页数据，供双击表打开的数据预览页使用。
        /// </summary>
        public virtual QueryExecutionResult PreviewTable(ConnectionProfile profile, DatabaseObjectNode tableNode, int pageIndex, int pageSize)
        {
            if (profile == null)
            {
                throw new InvalidOperationException("请先选择数据库连接。");
            }

            if (tableNode == null || string.IsNullOrWhiteSpace(tableNode.Name))
            {
                throw new InvalidOperationException("请选择需要预览的表或视图。");
            }

            int safePageIndex = Math.Max(1, pageIndex);
            int safePageSize = Math.Max(1, pageSize);
            ConnectionProfile previewProfile = profile.Clone();
            if ((ProviderType == DatabaseProviderType.SqlServer || ProviderType == DatabaseProviderType.MySQL) &&
                !string.IsNullOrWhiteSpace(tableNode.DatabaseName))
            {
                previewProfile.DatabaseName = tableNode.DatabaseName;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            QueryExecutionResult result = new QueryExecutionResult();
            using (IDbConnection connection = CreateConnection(previewProfile))
            {
                connection.Open();
                result.TotalRows = ReadTableTotalRows(connection, tableNode);
                List<dynamic> rows = connection.Query(BuildPreviewTableSql(tableNode, safePageIndex, safePageSize)).ToList();
                result.Table = ConvertRowsToDataTable(rows);
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Message = "已加载第 " + safePageIndex + " 页，返回 " + (result.Table == null ? 0 : result.Table.Rows.Count) + " 行，共 " + Math.Max(0, result.TotalRows) + " 行。";
            return result;
        }

        /// <summary>
        /// 读取表或视图的真实总行数，供分页控件显示总页数。
        /// </summary>
        protected virtual int ReadTableTotalRows(IDbConnection connection, DatabaseObjectNode tableNode)
        {
            object value = connection.ExecuteScalar(BuildCountTableSql(tableNode));
            if (value == null || value == DBNull.Value)
            {
                return 0;
            }

            long totalRows;
            if (!long.TryParse(value.ToString(), out totalRows))
            {
                return 0;
            }

            return totalRows > int.MaxValue ? int.MaxValue : (int)Math.Max(0, totalRows);
        }

        /// <summary>
        /// 根据数据库方言生成统计表总行数的 SQL。
        /// </summary>
        protected virtual string BuildCountTableSql(DatabaseObjectNode tableNode)
        {
            return "SELECT COUNT(*) FROM " + QuoteTableName(tableNode);
        }

        /// <summary>
        /// 根据表或视图节点生成默认查询语句。
        /// </summary>
        /// <param name="tableNode">表或视图节点。</param>
        /// <returns>默认查询语句；如果节点无效则返回空字符串。</returns>
        public virtual string BuildDefaultSelectSql(DatabaseObjectNode tableNode)
        {
            if (tableNode == null || string.IsNullOrWhiteSpace(tableNode.Name))
            {
                return string.Empty;
            }

            string objectName = FormatDefaultIdentifier(tableNode.Name, GetDefaultQuoteFunction());
            string schemaName = tableNode.SchemaName ?? string.Empty;

            if (ProviderType == DatabaseProviderType.SqlServer)
            {
                if (string.IsNullOrWhiteSpace(schemaName) || SameName(schemaName, "dbo"))
                {
                    return "SELECT * FROM " + objectName;
                }

                return "SELECT * FROM " + FormatDefaultIdentifier(schemaName, GetDefaultQuoteFunction()) + "." + objectName;
            }

            if (ProviderType == DatabaseProviderType.Oracle)
            {
                if (string.IsNullOrWhiteSpace(schemaName))
                {
                    return "SELECT * FROM " + objectName;
                }

                return "SELECT * FROM " + FormatDefaultIdentifier(schemaName, GetDefaultQuoteFunction()) + "." + objectName;
            }

            return "SELECT * FROM " + objectName;
        }

        /// <summary>
        /// 根据数据库方言生成表预览分页 SQL。
        /// </summary>
        protected virtual string BuildPreviewTableSql(DatabaseObjectNode tableNode, int pageIndex, int pageSize)
        {
            int offset = Math.Max(0, (pageIndex - 1) * pageSize);
            string tableName = QuoteTableName(tableNode);

            if (ProviderType == DatabaseProviderType.SqlServer)
            {
                if (pageIndex <= 1)
                {
                    return "SELECT TOP " + pageSize + " *\r\nFROM " + tableName;
                }

                return "SELECT *\r\nFROM " + tableName + "\r\nORDER BY " + BuildPreviewOrderBy(tableNode) + "\r\nOFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }

            if (ProviderType == DatabaseProviderType.Oracle)
            {
                if (pageIndex <= 1)
                {
                    return "SELECT *\r\nFROM " + tableName + "\r\nWHERE ROWNUM <= " + pageSize;
                }

                return "SELECT *\r\nFROM " + tableName + "\r\nORDER BY " + BuildPreviewOrderBy(tableNode) + "\r\nOFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }

            if (ProviderType == DatabaseProviderType.MySQL || ProviderType == DatabaseProviderType.SQLite)
            {
                return "SELECT *\r\nFROM " + tableName + "\r\nLIMIT " + pageSize + " OFFSET " + offset;
            }

            return "SELECT *\r\nFROM " + tableName;
        }

        /// <summary>
        /// 构造预览分页排序字段，优先使用元数据中的第一个字段。
        /// </summary>
        protected virtual string BuildPreviewOrderBy(DatabaseObjectNode tableNode)
        {
            if (tableNode != null)
            {
                DatabaseObjectNode columnNode = tableNode.Children.FirstOrDefault(item => item.Kind == DatabaseObjectKind.Column && !string.IsNullOrWhiteSpace(item.Name));
                if (columnNode != null)
                {
                    return QuoteColumnName(columnNode.Name);
                }
            }

            return "(SELECT NULL)";
        }

        /// <summary>
        /// 根据数据库类型生成带分隔符的字段名。
        /// </summary>
        protected virtual string QuoteColumnName(string columnName)
        {
            if (ProviderType == DatabaseProviderType.MySQL)
            {
                return QuoteBacktickName(columnName);
            }

            if (ProviderType == DatabaseProviderType.SQLite || ProviderType == DatabaseProviderType.Oracle)
            {
                return QuoteDoubleName(columnName);
            }

            return QuoteSqlServerName(columnName);
        }

        /// <summary>
        /// 根据数据库类型生成带分隔符的表名。
        /// </summary>
        protected virtual string QuoteTableName(DatabaseObjectNode tableNode)
        {
            string schemaName = tableNode == null ? string.Empty : tableNode.SchemaName;
            string objectName = tableNode == null ? string.Empty : tableNode.Name;
            string databaseName = tableNode == null ? string.Empty : tableNode.DatabaseName;

            if (ProviderType == DatabaseProviderType.SqlServer)
            {
                string safeSchemaName = string.IsNullOrWhiteSpace(schemaName) ? "dbo" : schemaName;
                if (string.IsNullOrWhiteSpace(databaseName))
                {
                    return QuoteSqlServerName(safeSchemaName) + "." + QuoteSqlServerName(objectName);
                }

                return QuoteSqlServerName(databaseName) + "." + QuoteSqlServerName(safeSchemaName) + "." + QuoteSqlServerName(objectName);
            }

            if (ProviderType == DatabaseProviderType.MySQL)
            {
                string safeSchemaName = string.IsNullOrWhiteSpace(schemaName) ? databaseName : schemaName;
                return string.IsNullOrWhiteSpace(safeSchemaName)
                    ? QuoteBacktickName(objectName)
                    : QuoteBacktickName(safeSchemaName) + "." + QuoteBacktickName(objectName);
            }

            if (ProviderType == DatabaseProviderType.SQLite)
            {
                string safeSchemaName = string.IsNullOrWhiteSpace(schemaName) ? databaseName : schemaName;
                return string.IsNullOrWhiteSpace(safeSchemaName)
                    ? QuoteDoubleName(objectName)
                    : QuoteDoubleName(safeSchemaName) + "." + QuoteDoubleName(objectName);
            }

            if (ProviderType == DatabaseProviderType.Oracle)
            {
                return string.IsNullOrWhiteSpace(schemaName)
                    ? QuoteDoubleName(objectName)
                    : QuoteDoubleName(schemaName) + "." + QuoteDoubleName(objectName);
            }

            return QuoteSqlServerName(objectName);
        }

        /// <summary>
        /// 转义 SQL Server 方括号标识符。
        /// </summary>
        protected static string QuoteSqlServerName(string name)
        {
            return "[" + (name ?? string.Empty).Replace("]", "]]") + "]";
        }

        /// <summary>
        /// 转义 MySQL 反引号标识符。
        /// </summary>
        protected static string QuoteBacktickName(string name)
        {
            return "`" + (name ?? string.Empty).Replace("`", "``") + "`";
        }

        /// <summary>
        /// 转义双引号标识符。
        /// </summary>
        protected static string QuoteDoubleName(string name)
        {
            return "\"" + (name ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// 将标识符转换为默认查询中更适合直接阅读的一种写法。
        /// </summary>
        /// <param name="name">标识符名称。</param>
        /// <param name="sqlServerQuote">SQL Server 的引用函数。</param>
        /// <param name="mySqlQuote">MySQL 的引用函数。</param>
        /// <param name="doubleQuote">双引号引用函数。</param>
        /// <returns>适合默认查询展示的标识符文本。</returns>
        private Func<string, string> GetDefaultQuoteFunction()
        {
            if (ProviderType == DatabaseProviderType.MySQL)
            {
                return QuoteBacktickName;
            }

            if (ProviderType == DatabaseProviderType.SQLite || ProviderType == DatabaseProviderType.Oracle)
            {
                return QuoteDoubleName;
            }

            return QuoteSqlServerName;
        }

        /// <summary>
        /// 将标识符按当前数据库类型转换为默认查询中更适合直接阅读的一种写法。
        /// </summary>
        /// <param name="name">标识符名称。</param>
        /// <param name="quote">当前数据库类型对应的引用函数。</param>
        /// <returns>适合默认查询展示的标识符文本。</returns>
        private static string FormatDefaultIdentifier(string name, Func<string, string> quote)
        {
            string safeName = name ?? string.Empty;
            if (IsSimpleIdentifier(safeName))
            {
                return safeName;
            }

            return quote == null ? safeName : quote(safeName);
        }

        /// <summary>
        /// 判断标识符是否可以直接裸写。
        /// </summary>
        private static bool IsSimpleIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (!(char.IsLetter(name[0]) || name[0] == '_'))
            {
                return false;
            }

            for (int index = 1; index < name.Length; index++)
            {
                char value = name[index];
                if (!char.IsLetterOrDigit(value) && value != '_' && value != '$')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 判断 SQL 是否属于查询类语句。
        /// </summary>
        protected virtual bool IsQuerySql(string sql)
        {
            string text = sql.TrimStart();
            string upper = text.ToUpperInvariant();
            return upper.StartsWith("SELECT") ||
                   upper.StartsWith("WITH") ||
                   upper.StartsWith("SHOW") ||
                   upper.StartsWith("DESC") ||
                   upper.StartsWith("DESCRIBE") ||
                   upper.StartsWith("PRAGMA") ||
                   upper.StartsWith("EXPLAIN");
        }

        /// <summary>
        /// 将 Dapper 动态行转换为 WPF DataGrid 可直接绑定的 DataTable。
        /// </summary>
        protected static DataTable ConvertRowsToDataTable(IEnumerable<dynamic> rows)
        {
            DataTable table = new DataTable();
            foreach (object row in rows)
            {
                IDictionary<string, object> values = row as IDictionary<string, object>;
                if (values == null)
                {
                    continue;
                }

                foreach (string columnName in values.Keys)
                {
                    if (!table.Columns.Contains(columnName))
                    {
                        table.Columns.Add(columnName, typeof(object));
                    }
                }

                DataRow dataRow = table.NewRow();
                foreach (KeyValuePair<string, object> pair in values)
                {
                    dataRow[pair.Key] = pair.Value ?? DBNull.Value;
                }

                table.Rows.Add(dataRow);
            }

            return table;
        }

        /// <summary>
        /// 创建信息提示节点，用于驱动未补齐时给 UI 明确反馈。
        /// </summary>
        protected static DatabaseObjectNode CreateInfoNode(string message)
        {
            return new DatabaseObjectNode
            {
                Name = message,
                Kind = DatabaseObjectKind.Info
            };
        }

        /// <summary>
        /// 创建统一的表结构结果表。
        /// </summary>
        protected static DataTable CreateSchemaTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("序号", typeof(int));
            table.Columns.Add("字段名", typeof(string));
            table.Columns.Add("主键", typeof(string));
            table.Columns.Add("类型", typeof(string));
            table.Columns.Add("可空", typeof(string));
            table.Columns.Add("默认值", typeof(string));
            table.Columns.Add("注释", typeof(string));
            return table;
        }

        /// <summary>
        /// 使用忽略大小写方式比较数据库对象名称。
        /// </summary>
        protected static bool SameName(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
