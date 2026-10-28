using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
        /// 在事务中写入表预览页产生的新增、修改和删除行。
        /// </summary>
        public virtual int ApplyTableChanges(ConnectionProfile profile, DatabaseObjectNode tableNode, DataTable changes)
        {
            if (profile == null || tableNode == null || tableNode.Kind != DatabaseObjectKind.Table)
            {
                throw new InvalidOperationException("只有具体数据表支持直接编辑。");
            }

            if (changes == null || changes.Rows.Count == 0)
            {
                return 0;
            }

            DataTable schema = LoadTableSchema(profile, tableNode);
            List<string> primaryKeys = new List<string>();
            foreach (DataRow schemaRow in schema.Rows)
            {
                string columnName = Convert.ToString(schemaRow["字段名"]);
                if (!string.IsNullOrWhiteSpace(columnName) && SameName(Convert.ToString(schemaRow["主键"]), "是"))
                {
                    primaryKeys.Add(columnName);
                }
            }

            // 无主键时使用原始字段值定位，不能使用用户刚编辑后的值。
            IList<string> matchColumns = primaryKeys.Count > 0
                ? primaryKeys
                : GetOriginalValueMatchColumns(changes, schema);

            ConnectionProfile writeProfile = profile.Clone();
            if ((ProviderType == DatabaseProviderType.SqlServer || ProviderType == DatabaseProviderType.MySQL) &&
                !string.IsNullOrWhiteSpace(tableNode.DatabaseName))
            {
                writeProfile.DatabaseName = tableNode.DatabaseName;
            }

            int affectedRows = 0;
            using (IDbConnection connection = CreateConnection(writeProfile))
            {
                connection.Open();
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (DataRow row in changes.Rows)
                        {
                            if (row.RowState == DataRowState.Added)
                            {
                                affectedRows += InsertChangedRow(connection, transaction, tableNode, row);
                            }
                            else if (row.RowState == DataRowState.Modified)
                            {
                                affectedRows += UpdateChangedRow(connection, transaction, tableNode, row, matchColumns, primaryKeys.Count == 0);
                            }
                            else if (row.RowState == DataRowState.Deleted)
                            {
                                affectedRows += DeleteChangedRow(connection, transaction, tableNode, row, matchColumns, primaryKeys.Count == 0);
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }

            return affectedRows;
        }

        private int InsertChangedRow(IDbConnection connection, IDbTransaction transaction, DatabaseObjectNode tableNode, DataRow row)
        {
            List<DataColumn> columns = row.Table.Columns.Cast<DataColumn>()
                .Where(column => row[column] != DBNull.Value)
                .ToList();
            if (columns.Count == 0)
            {
                throw new InvalidOperationException("新增行至少需要填写一个字段值。");
            }

            List<object> values = columns.Select(column => row[column]).ToList();
            string sql = "INSERT INTO " + QuoteTableName(tableNode) + " (" +
                         string.Join(", ", columns.Select(column => QuoteColumnName(column.ColumnName)).ToArray()) + ") VALUES (" +
                         BuildWriteParameterList(values.Count) + ")";
            return ExecuteTableChange(connection, transaction, sql, values);
        }

        private int UpdateChangedRow(IDbConnection connection, IDbTransaction transaction, DatabaseObjectNode tableNode, DataRow row, IList<string> matchColumns, bool matchOriginalValues)
        {
            List<DataColumn> modifiedColumns = row.Table.Columns.Cast<DataColumn>()
                .Where(column => !ValuesEqual(row[column, DataRowVersion.Original], row[column, DataRowVersion.Current]))
                .ToList();
            if (modifiedColumns.Count == 0)
            {
                return 0;
            }

            if (matchOriginalValues)
            {
                EnsureOriginalRowIsUnique(connection, transaction, tableNode, row, matchColumns);
            }

            List<object> values = new List<object>();
            List<string> assignments = new List<string>();
            foreach (DataColumn column in modifiedColumns)
            {
                assignments.Add(QuoteColumnName(column.ColumnName) + " = " + BuildWriteParameter(values.Count));
                values.Add(row[column, DataRowVersion.Current]);
            }

            string whereClause = BuildRowMatchWhere(row, matchColumns, DataRowVersion.Original, values);
            int affected = ExecuteTableChange(connection, transaction,
                "UPDATE " + QuoteTableName(tableNode) + " SET " + string.Join(", ", assignments.ToArray()) + " WHERE " + whereClause, values);
            EnsureSingleRowChanged(affected);
            return affected;
        }

        private int DeleteChangedRow(IDbConnection connection, IDbTransaction transaction, DatabaseObjectNode tableNode, DataRow row, IList<string> matchColumns, bool matchOriginalValues)
        {
            if (matchOriginalValues)
            {
                EnsureOriginalRowIsUnique(connection, transaction, tableNode, row, matchColumns);
            }

            List<object> values = new List<object>();
            string whereClause = BuildRowMatchWhere(row, matchColumns, DataRowVersion.Original, values);
            int affected = ExecuteTableChange(connection, transaction,
                "DELETE FROM " + QuoteTableName(tableNode) + " WHERE " + whereClause, values);
            EnsureSingleRowChanged(affected);
            return affected;
        }

        /// <summary>
        /// 大对象等不支持等值比较的字段不参与定位；剩余条件仍须在数据库中唯一匹配。
        /// </summary>
        private IList<string> GetOriginalValueMatchColumns(DataTable changes, DataTable schema)
        {
            List<string> columns = new List<string>();
            foreach (DataColumn column in changes.Columns)
            {
                string typeName = schema.Rows.Cast<DataRow>()
                    .Where(row => SameName(Convert.ToString(row["字段名"]), column.ColumnName))
                    .Select(row => Convert.ToString(row["类型"]).ToLowerInvariant())
                    .FirstOrDefault() ?? string.Empty;
                if (ProviderType == DatabaseProviderType.Access &&
                    (column.DataType == typeof(byte[]) || typeName == "ole 对象"))
                {
                    continue;
                }

                if (ProviderType == DatabaseProviderType.SqlServer &&
                    (typeName == "text" || typeName == "ntext" || typeName == "image" || typeName == "xml"))
                {
                    continue;
                }

                if (ProviderType == DatabaseProviderType.Oracle &&
                    (typeName.StartsWith("clob") || typeName.StartsWith("nclob") || typeName.StartsWith("blob") ||
                     typeName == "long" || typeName.StartsWith("long raw") || typeName.StartsWith("xmltype")))
                {
                    continue;
                }

                columns.Add(column.ColumnName);
            }

            return columns;
        }

        private void EnsureOriginalRowIsUnique(IDbConnection connection, IDbTransaction transaction, DatabaseObjectNode tableNode, DataRow row, IList<string> matchColumns)
        {
            List<object> values = new List<object>();
            string whereClause = BuildRowMatchWhere(row, matchColumns, DataRowVersion.Original, values);
            using (IDbCommand command = CreateTableChangeCommand(connection, transaction,
                "SELECT COUNT(*) FROM " + QuoteTableName(tableNode) + " WHERE " + whereClause, values))
            {
                long count = Convert.ToInt64(command.ExecuteScalar());
                if (count != 1)
                {
                    throw new InvalidOperationException(count == 0
                        ? "数据行已被其他操作修改或删除，请刷新后重试。"
                        : "该表没有主键，原始字段值匹配到多行，无法唯一定位当前行。本次更改已取消，请为表添加主键或唯一标识后重试。");
                }
            }
        }

        private string BuildRowMatchWhere(DataRow row, IList<string> matchColumns, DataRowVersion version, IList<object> values)
        {
            if (matchColumns.Count == 0)
            {
                throw new InvalidOperationException("该表没有主键，也没有可用于定位的普通字段，无法安全修改或删除数据行。");
            }

            List<string> conditions = new List<string>();
            foreach (string key in matchColumns)
            {
                if (!row.Table.Columns.Contains(key))
                {
                    throw new InvalidOperationException("结果中缺少主键字段 " + key + "，无法安全写入。");
                }

                object value = row[key, version];
                if (value == DBNull.Value)
                {
                    conditions.Add(QuoteColumnName(key) + " IS NULL");
                }
                else
                {
                    conditions.Add(QuoteColumnName(key) + " = " + BuildWriteParameter(values.Count));
                    values.Add(value);
                }
            }

            return string.Join(" AND ", conditions.ToArray());
        }

        private int ExecuteTableChange(IDbConnection connection, IDbTransaction transaction, string sql, IList<object> values)
        {
            using (IDbCommand command = CreateTableChangeCommand(connection, transaction, sql, values))
            {
                return command.ExecuteNonQuery();
            }
        }

        private IDbCommand CreateTableChangeCommand(IDbConnection connection, IDbTransaction transaction, string sql, IList<object> values)
        {
            IDbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            for (int index = 0; index < values.Count; index++)
            {
                IDbDataParameter parameter = command.CreateParameter();
                parameter.ParameterName = "p" + index;
                parameter.Value = values[index] ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            return command;
        }

        private string BuildWriteParameterList(int count)
        {
            List<string> parameters = new List<string>();
            for (int index = 0; index < count; index++)
            {
                parameters.Add(BuildWriteParameter(index));
            }

            return string.Join(", ", parameters.ToArray());
        }

        private string BuildWriteParameter(int index)
        {
            if (ProviderType == DatabaseProviderType.Access)
            {
                return "?";
            }

            return (ProviderType == DatabaseProviderType.Oracle ? ":" : "@") + "p" + index;
        }

        private static bool ValuesEqual(object left, object right)
        {
            return object.Equals(left == DBNull.Value ? null : left, right == DBNull.Value ? null : right);
        }

        private static void EnsureSingleRowChanged(int affectedRows)
        {
            if (affectedRows != 1)
            {
                throw new InvalidOperationException(affectedRows == 0
                    ? "数据行已被其他操作修改或删除，请刷新后重试。"
                    : "定位条件匹配到多行，事务已取消以避免误改数据。");
            }
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
            return PreviewTable(profile, tableNode, pageIndex, pageSize, null);
        }

        /// <summary>
        /// 按快速筛选条件读取表或视图的某一页数据，供双击表打开的数据预览页使用。
        /// </summary>
        public virtual QueryExecutionResult PreviewTable(ConnectionProfile profile, DatabaseObjectNode tableNode, int pageIndex, int pageSize, IList<TableFilterCondition> filters)
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
            TableFilterSqlBuildResult filterSql = BuildTableFilterWhereClause(tableNode, filters, false);
            DynamicParameters parameters = CreateDynamicParameters(filterSql.Parameters);
            using (IDbConnection connection = CreateConnection(previewProfile))
            {
                connection.Open();
                result.TotalRows = ReadTableTotalRows(connection, tableNode, filterSql.WhereClause, parameters);
                List<dynamic> rows = connection.Query(BuildPreviewTableSql(tableNode, safePageIndex, safePageSize, filterSql.WhereClause), parameters).ToList();
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
            return ReadTableTotalRows(connection, tableNode, string.Empty, null);
        }

        /// <summary>
        /// 按快速筛选条件读取表或视图的真实总行数，供分页控件显示总页数。
        /// </summary>
        protected virtual int ReadTableTotalRows(IDbConnection connection, DatabaseObjectNode tableNode, string whereClause, object parameters)
        {
            object value = connection.ExecuteScalar(BuildCountTableSql(tableNode, whereClause), parameters);
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
            return BuildCountTableSql(tableNode, string.Empty);
        }

        /// <summary>
        /// 根据数据库方言生成带快速筛选条件的统计表总行数 SQL。
        /// </summary>
        protected virtual string BuildCountTableSql(DatabaseObjectNode tableNode, string whereClause)
        {
            return "SELECT COUNT(*) FROM " + QuoteTableName(tableNode) + NormalizeWhereClause(whereClause);
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
            return BuildPreviewTableSql(tableNode, pageIndex, pageSize, string.Empty);
        }

        /// <summary>
        /// 根据数据库方言生成带快速筛选条件的表预览分页 SQL。
        /// </summary>
        protected virtual string BuildPreviewTableSql(DatabaseObjectNode tableNode, int pageIndex, int pageSize, string whereClause)
        {
            int offset = Math.Max(0, (pageIndex - 1) * pageSize);
            string tableName = QuoteTableName(tableNode);
            string safeWhereClause = NormalizeWhereClause(whereClause);

            if (ProviderType == DatabaseProviderType.SqlServer)
            {
                if (pageIndex <= 1)
                {
                    return "SELECT TOP " + pageSize + " *\r\nFROM " + tableName + safeWhereClause;
                }

                return "SELECT *\r\nFROM " + tableName + safeWhereClause + "\r\nORDER BY " + BuildPreviewOrderBy(tableNode) + "\r\nOFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }

            if (ProviderType == DatabaseProviderType.Oracle)
            {
                if (pageIndex <= 1)
                {
                    return "SELECT *\r\nFROM " + tableName + BuildOracleFirstPageWhereClause(safeWhereClause, pageSize);
                }

                return "SELECT *\r\nFROM " + tableName + safeWhereClause + "\r\nORDER BY " + BuildPreviewOrderBy(tableNode) + "\r\nOFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }

            if (ProviderType == DatabaseProviderType.MySQL || ProviderType == DatabaseProviderType.SQLite)
            {
                return "SELECT *\r\nFROM " + tableName + safeWhereClause + "\r\nLIMIT " + pageSize + " OFFSET " + offset;
            }

            return "SELECT *\r\nFROM " + tableName + safeWhereClause;
        }

        /// <summary>
        /// 为 Oracle 首页预览合并用户筛选条件和 ROWNUM 条件。
        /// </summary>
        private static string BuildOracleFirstPageWhereClause(string whereClause, int pageSize)
        {
            if (string.IsNullOrWhiteSpace(whereClause))
            {
                return "\r\nWHERE ROWNUM <= " + pageSize;
            }

            return whereClause + " AND ROWNUM <= " + pageSize;
        }

        /// <summary>
        /// 根据快速筛选条件生成 WHERE 子句和参数集合。
        /// </summary>
        protected virtual TableFilterSqlBuildResult BuildTableFilterWhereClause(DatabaseObjectNode tableNode, IList<TableFilterCondition> filters, bool usePositionalParameters)
        {
            TableFilterSqlBuildResult result = new TableFilterSqlBuildResult();
            if (tableNode == null || filters == null || filters.Count == 0)
            {
                return result;
            }

            StringBuilder builder = new StringBuilder();
            int activeCount = 0;
            foreach (TableFilterCondition filter in filters)
            {
                string conditionSql = BuildSingleFilterConditionSql(tableNode, filter, result, usePositionalParameters);
                if (string.IsNullOrWhiteSpace(conditionSql))
                {
                    continue;
                }

                if (activeCount > 0)
                {
                    builder.Append(" ").Append(NormalizeFilterLogic(filter == null ? string.Empty : filter.Logic)).Append(" ");
                }

                builder.Append("(").Append(conditionSql).Append(")");
                activeCount++;
            }

            if (activeCount > 0)
            {
                result.WhereClause = "\r\nWHERE " + builder;
            }

            return result;
        }

        /// <summary>
        /// 根据单条筛选条件生成 SQL 片段。
        /// </summary>
        private string BuildSingleFilterConditionSql(DatabaseObjectNode tableNode, TableFilterCondition filter, TableFilterSqlBuildResult result, bool usePositionalParameters)
        {
            if (filter == null || !filter.IsEnabled || string.IsNullOrWhiteSpace(filter.ColumnName))
            {
                return string.Empty;
            }

            DatabaseObjectNode columnNode = FindFilterColumn(tableNode, filter.ColumnName);
            if (columnNode == null)
            {
                return string.Empty;
            }

            string columnSql = QuoteColumnName(columnNode.Name);
            string textColumnSql = BuildTextColumnExpression(columnSql);
            string operatorCode = string.IsNullOrWhiteSpace(filter.OperatorCode) ? "Contains" : filter.OperatorCode;
            string value = filter.Value == null ? string.Empty : filter.Value.Trim();
            if (IsFilterValueRequired(operatorCode) && string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (SameName(operatorCode, "Equal"))
            {
                return columnSql + " = " + AddFilterParameter(result, value, usePositionalParameters);
            }

            if (SameName(operatorCode, "NotEqual"))
            {
                return columnSql + " <> " + AddFilterParameter(result, value, usePositionalParameters);
            }

            if (SameName(operatorCode, "LessThan"))
            {
                return columnSql + " < " + AddFilterParameter(result, value, usePositionalParameters);
            }

            if (SameName(operatorCode, "LessThanOrEqual"))
            {
                return columnSql + " <= " + AddFilterParameter(result, value, usePositionalParameters);
            }

            if (SameName(operatorCode, "GreaterThan"))
            {
                return columnSql + " > " + AddFilterParameter(result, value, usePositionalParameters);
            }

            if (SameName(operatorCode, "GreaterThanOrEqual"))
            {
                return columnSql + " >= " + AddFilterParameter(result, value, usePositionalParameters);
            }

            if (SameName(operatorCode, "Contains"))
            {
                return textColumnSql + " LIKE " + AddFilterParameter(result, BuildLikeValue(value, true, true), usePositionalParameters);
            }

            if (SameName(operatorCode, "NotContains"))
            {
                return textColumnSql + " NOT LIKE " + AddFilterParameter(result, BuildLikeValue(value, true, true), usePositionalParameters);
            }

            if (SameName(operatorCode, "StartsWith"))
            {
                return textColumnSql + " LIKE " + AddFilterParameter(result, BuildLikeValue(value, false, true), usePositionalParameters);
            }

            if (SameName(operatorCode, "NotStartsWith"))
            {
                return textColumnSql + " NOT LIKE " + AddFilterParameter(result, BuildLikeValue(value, false, true), usePositionalParameters);
            }

            if (SameName(operatorCode, "EndsWith"))
            {
                return textColumnSql + " LIKE " + AddFilterParameter(result, BuildLikeValue(value, true, false), usePositionalParameters);
            }

            if (SameName(operatorCode, "NotEndsWith"))
            {
                return textColumnSql + " NOT LIKE " + AddFilterParameter(result, BuildLikeValue(value, true, false), usePositionalParameters);
            }

            if (SameName(operatorCode, "IsNull"))
            {
                return columnSql + " IS NULL";
            }

            if (SameName(operatorCode, "IsNotNull"))
            {
                return columnSql + " IS NOT NULL";
            }

            if (SameName(operatorCode, "IsEmpty"))
            {
                return textColumnSql + " = " + AddFilterParameter(result, string.Empty, usePositionalParameters);
            }

            if (SameName(operatorCode, "IsNotEmpty"))
            {
                return textColumnSql + " <> " + AddFilterParameter(result, string.Empty, usePositionalParameters);
            }

            if (SameName(operatorCode, "Between"))
            {
                IList<string> values = SplitFilterValues(value);
                if (values.Count < 2)
                {
                    return string.Empty;
                }

                return columnSql + " BETWEEN " + AddFilterParameter(result, values[0], usePositionalParameters) + " AND " + AddFilterParameter(result, values[1], usePositionalParameters);
            }

            if (SameName(operatorCode, "In") || SameName(operatorCode, "NotIn"))
            {
                IList<string> values = SplitFilterValues(value);
                if (values.Count == 0)
                {
                    return string.Empty;
                }

                List<string> placeholders = new List<string>();
                foreach (string item in values)
                {
                    placeholders.Add(AddFilterParameter(result, item, usePositionalParameters));
                }

                return columnSql + (SameName(operatorCode, "NotIn") ? " NOT IN (" : " IN (") + string.Join(", ", placeholders.ToArray()) + ")";
            }

            return textColumnSql + " LIKE " + AddFilterParameter(result, BuildLikeValue(value, true, true), usePositionalParameters);
        }

        /// <summary>
        /// 查找当前表中真实存在的字段，防止筛选列名被手工篡改后直接拼进 SQL。
        /// </summary>
        private static DatabaseObjectNode FindFilterColumn(DatabaseObjectNode tableNode, string columnName)
        {
            if (tableNode == null || string.IsNullOrWhiteSpace(columnName))
            {
                return null;
            }

            foreach (DatabaseObjectNode child in tableNode.Children)
            {
                if (child.Kind == DatabaseObjectKind.Column && SameName(child.Name, columnName))
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// 判断筛选运算符是否必须输入筛选值。
        /// </summary>
        private static bool IsFilterValueRequired(string operatorCode)
        {
            return !SameName(operatorCode, "IsNull") &&
                   !SameName(operatorCode, "IsNotNull") &&
                   !SameName(operatorCode, "IsEmpty") &&
                   !SameName(operatorCode, "IsNotEmpty");
        }

        /// <summary>
        /// 规范化多条件之间的逻辑关系。
        /// </summary>
        private static string NormalizeFilterLogic(string logic)
        {
            return SameName(logic, "OR") ? "OR" : "AND";
        }

        /// <summary>
        /// 将筛选值拆成列表，供介于、在列表、不在列表等运算符使用。
        /// </summary>
        private static IList<string> SplitFilterValues(string value)
        {
            List<string> values = new List<string>();
            string text = value ?? string.Empty;
            string normalized = text.Replace("，", ",").Replace("；", ",").Replace(";", ",").Replace("\r", "\n");
            foreach (string item in normalized.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string safeItem = item.Trim();
                if (!string.IsNullOrWhiteSpace(safeItem))
                {
                    values.Add(safeItem);
                }
            }

            if (values.Count < 2)
            {
                string upper = text.ToUpperInvariant();
                int andIndex = upper.IndexOf(" AND ", StringComparison.Ordinal);
                if (andIndex > 0)
                {
                    values.Clear();
                    string first = text.Substring(0, andIndex).Trim();
                    string second = text.Substring(andIndex + 5).Trim();
                    if (!string.IsNullOrWhiteSpace(first))
                    {
                        values.Add(first);
                    }

                    if (!string.IsNullOrWhiteSpace(second))
                    {
                        values.Add(second);
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// 根据数据库类型生成 LIKE 查询的文本化字段表达式。
        /// </summary>
        protected virtual string BuildTextColumnExpression(string quotedColumnName)
        {
            if (ProviderType == DatabaseProviderType.SqlServer)
            {
                return "CONVERT(NVARCHAR(MAX), " + quotedColumnName + ")";
            }

            if (ProviderType == DatabaseProviderType.MySQL)
            {
                return "CAST(" + quotedColumnName + " AS CHAR)";
            }

            if (ProviderType == DatabaseProviderType.SQLite)
            {
                return "CAST(" + quotedColumnName + " AS TEXT)";
            }

            if (ProviderType == DatabaseProviderType.Oracle)
            {
                return "TO_CHAR(" + quotedColumnName + ")";
            }

            if (ProviderType == DatabaseProviderType.Access)
            {
                return "(" + quotedColumnName + " & '')";
            }

            return quotedColumnName;
        }

        /// <summary>
        /// 根据数据库类型生成 LIKE 参数值。
        /// </summary>
        protected virtual string BuildLikeValue(string value, bool startsWithWildcard, bool endsWithWildcard)
        {
            string wildcard = "%";
            string safeValue = value ?? string.Empty;
            return (startsWithWildcard ? wildcard : string.Empty) + safeValue + (endsWithWildcard ? wildcard : string.Empty);
        }

        /// <summary>
        /// 新增一个筛选参数，并返回当前数据库 SQL 中对应的占位符。
        /// </summary>
        private string AddFilterParameter(TableFilterSqlBuildResult result, object value, bool usePositionalParameters)
        {
            string parameterName = "FilterValue" + result.Parameters.Count;
            result.Parameters.Add(new TableFilterParameter(parameterName, value));
            if (usePositionalParameters)
            {
                return "?";
            }

            return GetSqlParameterPrefix() + parameterName;
        }

        /// <summary>
        /// 获取当前数据库方言的参数前缀。
        /// </summary>
        private string GetSqlParameterPrefix()
        {
            return ProviderType == DatabaseProviderType.Oracle ? ":" : "@";
        }

        /// <summary>
        /// 将通用参数集合转换成 Dapper 可识别的参数对象。
        /// </summary>
        private static DynamicParameters CreateDynamicParameters(IList<TableFilterParameter> parameters)
        {
            DynamicParameters dynamicParameters = new DynamicParameters();
            if (parameters == null)
            {
                return dynamicParameters;
            }

            foreach (TableFilterParameter parameter in parameters)
            {
                dynamicParameters.Add(parameter.Name, parameter.Value);
            }

            return dynamicParameters;
        }

        /// <summary>
        /// 规范化 WHERE 子句，避免拼接出多余空白。
        /// </summary>
        protected static string NormalizeWhereClause(string whereClause)
        {
            return string.IsNullOrWhiteSpace(whereClause) ? string.Empty : whereClause;
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
        /// 表快速筛选生成后的 WHERE 子句和参数集合。
        /// </summary>
        protected class TableFilterSqlBuildResult
        {
            /// <summary>
            /// WHERE 子句，空字符串表示没有启用筛选。
            /// </summary>
            public string WhereClause { get; set; }

            /// <summary>
            /// 参数集合，顺序与 SQL 中占位符出现顺序一致。
            /// </summary>
            public IList<TableFilterParameter> Parameters { get; private set; }

            /// <summary>
            /// 初始化筛选 SQL 构造结果。
            /// </summary>
            public TableFilterSqlBuildResult()
            {
                WhereClause = string.Empty;
                Parameters = new List<TableFilterParameter>();
            }
        }

        /// <summary>
        /// 表快速筛选 SQL 参数。
        /// </summary>
        protected class TableFilterParameter
        {
            /// <summary>
            /// 参数名称。
            /// </summary>
            public string Name { get; private set; }

            /// <summary>
            /// 参数值。
            /// </summary>
            public object Value { get; private set; }

            /// <summary>
            /// 初始化筛选参数。
            /// </summary>
            public TableFilterParameter(string name, object value)
            {
                Name = name;
                Value = value;
            }
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
