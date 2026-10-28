using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Dapper;
using TXDB.Models;

namespace TXDB.Data
{
    public sealed class TableSchemaSnapshot
    {
        public List<TableColumnDefinition> Columns { get; set; } = new List<TableColumnDefinition>();
        public string PrimaryKeyName { get; set; }
        public string CreateSql { get; set; }
    }

    /// <summary>表设计器的结构读取、校验和各数据库 DDL 操作。</summary>
    public sealed class TableSchemaEditor
    {
        private readonly IDatabaseProvider provider;
        private DatabaseProviderType Kind { get { return provider.ProviderType; } }

        public TableSchemaEditor(IDatabaseProvider provider) { this.provider = provider; }

        public string[] GetTypeNames()
        {
            switch (Kind)
            {
                case DatabaseProviderType.Access:
                    return new[] { "短文本", "长文本", "字节", "整型", "长整型", "大整数", "单精度浮点型", "双精度浮点型", "十进制数", "货币", "是/否", "日期/时间", "GUID", "二进制", "OLE 对象" };
                case DatabaseProviderType.SqlServer:
                    return new[] { "int", "bigint", "smallint", "tinyint", "bit", "decimal", "numeric", "float", "real", "money", "nvarchar", "varchar", "nchar", "char", "date", "datetime", "datetime2", "datetimeoffset", "time", "uniqueidentifier", "varbinary", "binary", "xml" };
                case DatabaseProviderType.MySQL:
                    return new[] { "int", "bigint", "smallint", "tinyint", "mediumint", "decimal", "float", "double", "bit", "boolean", "varchar", "char", "text", "mediumtext", "longtext", "date", "datetime", "timestamp", "time", "year", "binary", "varbinary", "blob", "json" };
                case DatabaseProviderType.Oracle:
                    return new[] { "NUMBER", "FLOAT", "BINARY_FLOAT", "BINARY_DOUBLE", "VARCHAR2", "NVARCHAR2", "CHAR", "NCHAR", "DATE", "TIMESTAMP", "TIMESTAMP WITH TIME ZONE", "CLOB", "NCLOB", "BLOB", "RAW" };
                default:
                    return new[] { "INTEGER", "TEXT", "REAL", "NUMERIC", "BLOB", "VARCHAR", "CHAR", "BOOLEAN", "DATE", "DATETIME", "DECIMAL" };
            }
        }

        public ConnectionProfile GetScopedProfile(ConnectionProfile profile, DatabaseObjectNode node)
        {
            ConnectionProfile scoped = profile.Clone();
            if ((Kind == DatabaseProviderType.SqlServer || Kind == DatabaseProviderType.MySQL) && !string.IsNullOrWhiteSpace(node.DatabaseName))
            {
                scoped.DatabaseName = node.DatabaseName;
                if (!string.IsNullOrWhiteSpace(scoped.RawConnectionString))
                {
                    if (Kind == DatabaseProviderType.SqlServer)
                    {
                        var builder = new System.Data.SqlClient.SqlConnectionStringBuilder(scoped.RawConnectionString);
                        builder.InitialCatalog = node.DatabaseName;
                        scoped.RawConnectionString = builder.ConnectionString;
                    }
                    else
                    {
                        var builder = new MySqlConnector.MySqlConnectionStringBuilder(scoped.RawConnectionString);
                        builder.Database = node.DatabaseName;
                        scoped.RawConnectionString = builder.ConnectionString;
                    }
                }
            }
            return scoped;
        }

        public TableSchemaSnapshot Load(ConnectionProfile profile, DatabaseObjectNode node)
        {
            if (Kind == DatabaseProviderType.SqlServer && string.IsNullOrWhiteSpace(node.SchemaName))
                node = new DatabaseObjectNode { Name = node.Name, DatabaseName = node.DatabaseName, SchemaName = "dbo", Kind = node.Kind };
            ConnectionProfile scoped = GetScopedProfile(profile, node);
            DataTable schema = ((IDatabaseMetadataProvider)provider).LoadTableSchema(scoped, node);
            TableSchemaSnapshot result = new TableSchemaSnapshot();
            foreach (DataRow row in schema.Rows)
            {
                string type = Convert.ToString(row["类型"]);
                Match match = Regex.Match(type, @"^(.*?)\((.*)\)(.*)$");
                TableColumnDefinition column = new TableColumnDefinition
                {
                    OriginalName = Convert.ToString(row["字段名"]),
                    Name = Convert.ToString(row["字段名"]),
                    TypeName = match.Success ? (match.Groups[1].Value + match.Groups[3].Value).Trim() : type,
                    TypeArguments = match.Success ? match.Groups[2].Value : string.Empty,
                    AllowNull = IsTrue(row["可空"]),
                    IsPrimaryKey = IsTrue(row["主键"]),
                    DefaultSql = Convert.ToString(row["默认值"]),
                    Comment = Convert.ToString(row["注释"])
                };
                result.Columns.Add(column);
            }

            using (IDbConnection connection = provider.CreateConnection(scoped))
            {
                connection.Open();
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM " + TableName(node) + " WHERE 1=0";
                    using (IDataReader reader = command.ExecuteReader(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo))
                    {
                        DataTable details = reader.GetSchemaTable();
                        if (details != null)
                        {
                            foreach (DataRow detail in details.Rows)
                            {
                                TableColumnDefinition column = result.Columns.FirstOrDefault(c => c.Name == Convert.ToString(detail["ColumnName"]));
                                if (column == null) continue;
                                column.IsIdentity = details.Columns.Contains("IsAutoIncrement") && IsTrue(detail["IsAutoIncrement"]);
                                column.IsComputed = details.Columns.Contains("IsReadOnly") && IsTrue(detail["IsReadOnly"]) && !column.IsIdentity;
                            }
                        }
                    }
                }
                LoadExtraMetadata(connection, node, result);
            }
            return result;
        }

        private void LoadExtraMetadata(IDbConnection connection, DatabaseObjectNode node, TableSchemaSnapshot result)
        {
            if (Kind == DatabaseProviderType.Access)
            {
                DataTable keys = ((System.Data.OleDb.OleDbConnection)connection).GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Primary_Keys, new object[] { null, null, node.Name });
                if (keys != null && keys.Rows.Count > 0 && keys.Columns.Contains("PK_NAME")) result.PrimaryKeyName = Convert.ToString(keys.Rows[0]["PK_NAME"]);
            }
            else if (Kind == DatabaseProviderType.SqlServer)
            {
                string schema = string.IsNullOrEmpty(node.SchemaName) ? "dbo" : node.SchemaName;
                result.PrimaryKeyName = connection.ExecuteScalar<string>("SELECT kc.name FROM sys.key_constraints kc JOIN sys.tables t ON kc.parent_object_id=t.object_id JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE kc.type='PK' AND s.name=@schema AND t.name=@table", new { schema, table = node.Name });
                foreach (dynamic row in connection.Query("SELECT c.name, c.is_identity, c.is_computed, c.collation_name, c.scale, dc.name AS DefaultName, CASE WHEN ep.name IS NULL THEN 0 ELSE 1 END AS HasComment FROM sys.columns c JOIN sys.tables t ON c.object_id=t.object_id JOIN sys.schemas s ON t.schema_id=s.schema_id LEFT JOIN sys.default_constraints dc ON c.default_object_id=dc.object_id LEFT JOIN sys.extended_properties ep ON ep.class=1 AND ep.major_id=c.object_id AND ep.minor_id=c.column_id AND ep.name='MS_Description' WHERE s.name=@schema AND t.name=@table", new { schema, table = node.Name }))
                {
                    TableColumnDefinition column = result.Columns.FirstOrDefault(c => c.Name == (string)row.name);
                    if (column == null) continue;
                    column.IsIdentity = Convert.ToBoolean(row.is_identity);
                    column.IsComputed = Convert.ToBoolean(row.is_computed) || column.TypeName == "timestamp" || column.TypeName == "rowversion";
                    column.Collation = row.collation_name;
                    column.DefaultConstraintName = row.DefaultName;
                    column.HasCommentProperty = Convert.ToBoolean(row.HasComment);
                    if (new[] { "datetime2", "datetimeoffset", "time" }.Contains(column.TypeName.ToLowerInvariant())) column.TypeArguments = Convert.ToString(row.scale);
                }
            }
            else if (Kind == DatabaseProviderType.MySQL)
            {
                string schema = string.IsNullOrEmpty(node.SchemaName) ? node.DatabaseName : node.SchemaName;
                foreach (dynamic row in connection.Query("SELECT COLUMN_NAME, COLUMN_DEFAULT, EXTRA, COLLATION_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table", new { schema, table = node.Name }))
                {
                    TableColumnDefinition column = result.Columns.FirstOrDefault(c => c.Name == (string)row.COLUMN_NAME);
                    if (column == null) continue;
                    string extra = Convert.ToString(row.EXTRA);
                    column.IsIdentity = extra.IndexOf("auto_increment", StringComparison.OrdinalIgnoreCase) >= 0;
                    column.IsComputed = extra.IndexOf("GENERATED", StringComparison.OrdinalIgnoreCase) >= 0 && extra.IndexOf("DEFAULT_GENERATED", StringComparison.OrdinalIgnoreCase) < 0;
                    column.Extra = Regex.Replace(extra, @"(?i)auto_increment|DEFAULT_GENERATED", "").Trim();
                    column.Collation = row.COLLATION_NAME;
                    if (row.COLUMN_DEFAULT != null)
                    {
                        string value = Convert.ToString(row.COLUMN_DEFAULT);
                        column.DefaultSql = extra.IndexOf("DEFAULT_GENERATED", StringComparison.OrdinalIgnoreCase) >= 0 || Regex.IsMatch(value, @"(?i)^current_timestamp(?:\(\d*\))?$") ? value : Literal(value);
                    }
                }
                result.PrimaryKeyName = result.Columns.Any(c => c.IsPrimaryKey) ? "PRIMARY" : null;
            }
            else if (Kind == DatabaseProviderType.Oracle)
            {
                string owner = string.IsNullOrEmpty(node.SchemaName) ? connection.ExecuteScalar<string>("SELECT USER FROM DUAL") : node.SchemaName;
                result.PrimaryKeyName = connection.ExecuteScalar<string>("SELECT CONSTRAINT_NAME FROM ALL_CONSTRAINTS WHERE OWNER=:owner AND TABLE_NAME=:tableName AND CONSTRAINT_TYPE='P'", new { owner, tableName = node.Name });
                foreach (dynamic row in connection.Query("SELECT COLUMN_NAME, VIRTUAL_COLUMN FROM ALL_TAB_COLS WHERE OWNER=:owner AND TABLE_NAME=:tableName", new { owner, tableName = node.Name }))
                {
                    TableColumnDefinition column = result.Columns.FirstOrDefault(c => c.Name == (string)row.COLUMN_NAME);
                    if (column != null) column.IsComputed = (string)row.VIRTUAL_COLUMN == "YES";
                }
                var versionProperty = connection.GetType().GetProperty("ServerVersion");
                string version = versionProperty == null ? string.Empty : Convert.ToString(versionProperty.GetValue(connection, null));
                int major;
                if (int.TryParse(version.Split('.')[0], out major) && major >= 12)
                {
                    foreach (dynamic row in connection.Query("SELECT COLUMN_NAME FROM ALL_TAB_COLS WHERE OWNER=:owner AND TABLE_NAME=:tableName AND IDENTITY_COLUMN='YES'", new { owner, tableName = node.Name }))
                    {
                        TableColumnDefinition column = result.Columns.FirstOrDefault(c => c.Name == (string)row.COLUMN_NAME);
                        if (column != null) { column.IsIdentity = true; column.DefaultSql = string.Empty; }
                    }
                }
            }
            else
            {
                result.CreateSql = connection.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE type='table' AND name=@name", new { name = node.Name });
                foreach (TableColumnDefinition column in result.Columns) column.IsIdentity = false;
                if (!string.IsNullOrEmpty(result.CreateSql) && SqlDefinitionParser.Tokens(result.CreateSql).Any(t => !t.Quoted && t.Value.Equals("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (TableColumnDefinition column in result.Columns.Where(c => c.IsPrimaryKey && c.TypeName.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))) column.IsIdentity = true;
                }
            }
        }

        public void Validate(DatabaseObjectNode node, IList<TableColumnDefinition> columns, TableSchemaSnapshot original)
        {
            ValidateName(node.Name, "表名");
            if (node.Kind != DatabaseObjectKind.Table) throw new InvalidOperationException("视图仅支持查看结构。");
            if ((Kind == DatabaseProviderType.SqlServer || Kind == DatabaseProviderType.MySQL) && string.IsNullOrWhiteSpace(node.DatabaseName)) throw new InvalidOperationException("请选择或填写目标数据库。");
            if (columns.Count == 0) throw new InvalidOperationException("数据表至少需要一个字段。");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TableColumnDefinition column in columns)
            {
                ValidateName(column.Name, "字段名");
                if (!names.Add(column.Name)) throw new InvalidOperationException("字段名重复：" + column.Name);
                if (!string.IsNullOrEmpty(column.OriginalName) && !originals.Add(column.OriginalName)) throw new InvalidOperationException("原始字段重复：" + column.OriginalName);
                TableColumnDefinition before = original == null ? null : original.Columns.FirstOrDefault(c => c.Name == column.OriginalName);
                if (!string.IsNullOrEmpty(column.OriginalName) && before == null) throw new InvalidOperationException("原始字段已不存在，请刷新结构。");
                if (before != null && before.IsComputed && !column.SameDefinition(before)) throw new InvalidOperationException("计算字段暂不支持修改定义：" + column.Name);
                if (before != null && column.IsIdentity != before.IsIdentity) throw new InvalidOperationException("现有字段不能直接切换自增属性，请新增自增字段。");
                if (before == null || !column.SameDefinition(before))
                {
                    BuildType(column);
                    ValidateExpression(column.DefaultSql);
                }
                if (column.IsPrimaryKey && column.AllowNull) throw new InvalidOperationException("主键字段不能允许空值：" + column.Name);
                if (column.IsIdentity && !string.IsNullOrWhiteSpace(column.DefaultSql)) throw new InvalidOperationException("自增字段不能另外设置默认值：" + column.Name);
                if (column.IsIdentity && before == null)
                {
                    string type = column.TypeName.ToLowerInvariant();
                    if (Kind == DatabaseProviderType.Access && type != "长整型") throw new InvalidOperationException("Access 自增字段请选择“长整型”。");
                    if (Kind == DatabaseProviderType.SQLite && (type != "integer" || !column.IsPrimaryKey)) throw new InvalidOperationException("SQLite 自增字段必须是 INTEGER 主键。");
                    if (Kind == DatabaseProviderType.SqlServer && !new[] { "int", "bigint", "smallint", "tinyint", "decimal", "numeric" }.Contains(type)) throw new InvalidOperationException("SQL Server 自增字段必须使用整数类型或小数位数为 0 的 decimal/numeric。");
                    if (Kind == DatabaseProviderType.MySQL && !column.IsPrimaryKey) throw new InvalidOperationException("MySQL 新建自增字段请同时勾选主键。");
                }
            }
            if (columns.Count(c => c.IsIdentity) > 1) throw new InvalidOperationException("一张表只能设置一个自增字段。");
            if (Kind == DatabaseProviderType.SQLite && columns.Any(c => c.IsIdentity) && columns.Count(c => c.IsPrimaryKey) != 1) throw new InvalidOperationException("SQLite 自增字段不能与其他字段组成联合主键。");
            if (original != null)
            {
                foreach (TableColumnDefinition column in columns.Where(c => !string.IsNullOrEmpty(c.OriginalName) && c.Name != c.OriginalName))
                {
                    if (original.Columns.Any(c => c.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("字段重命名不能占用现有字段名称，请分次提交：" + column.Name);
                }
            }
        }

        public void Save(ConnectionProfile profile, DatabaseObjectNode node, TableSchemaSnapshot original, IList<TableColumnDefinition> columns)
        {
            Validate(node, columns, original);
            if (original != null)
            {
                TableSchemaSnapshot current = Load(profile, node);
                if (current.Columns.Count != original.Columns.Count || current.Columns.Where((c, i) => !c.SameDefinition(original.Columns[i])).Any() || current.CreateSql != original.CreateSql)
                    throw new InvalidOperationException("数据库表结构已被其他操作修改，请刷新后重新编辑。");
            }
            using (IDbConnection connection = provider.CreateConnection(GetScopedProfile(profile, node)))
            {
                connection.Open();
                if (Kind == DatabaseProviderType.SQLite && original != null)
                {
                    SqliteTableRebuilder.Save(connection, node, original, columns, this);
                    return;
                }
                List<string> statements = BuildStatements(node, original, columns);
                if (Kind == DatabaseProviderType.Access)
                {
                    SaveAccess(profile, node, original, columns, statements);
                    return;
                }
                bool transactional = Kind == DatabaseProviderType.SqlServer || Kind == DatabaseProviderType.SQLite;
                using (IDbTransaction transaction = transactional ? connection.BeginTransaction() : null)
                {
                    try
                    {
                        foreach (string sql in statements) connection.Execute(sql, transaction: transaction);
                        if (transaction != null) transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (transaction != null) transaction.Rollback();
                        if (!transactional) throw new InvalidOperationException("结构提交失败。该数据库的结构更改可能已部分生效，请刷新检查后再操作。" + Environment.NewLine + ex.Message, ex);
                        throw;
                    }
                }
            }
        }

        public void DropTable(ConnectionProfile profile, DatabaseObjectNode node)
        {
            if (node == null || node.Kind != DatabaseObjectKind.Table) throw new InvalidOperationException("请选择要删除的数据表。");
            ValidateName(node.Name, "表名");
            using (IDbConnection connection = provider.CreateConnection(GetScopedProfile(profile, node)))
            {
                connection.Open();
                // SQLite 的 DROP TABLE 可能触发 ON DELETE CASCADE，先阻止被其他表引用的表。
                if (Kind == DatabaseProviderType.SQLite)
                {
                    var tables = connection.Query<string>("SELECT name FROM sqlite_master WHERE type='table' AND name<>@name", new { name = node.Name }).ToList();
                    foreach (string other in tables)
                    {
                        if (connection.Query("PRAGMA foreign_key_list(" + Q(other) + ")").Any(row => string.Equals(Convert.ToString(((IDictionary<string, object>)row)["table"]), node.Name, StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException("表“" + node.Name + "”被“" + other + "”的外键引用，请先处理相关依赖后再删除。");
                    }
                    connection.Execute("PRAGMA foreign_keys=ON");
                }
                connection.Execute("DROP TABLE " + TableName(node));
            }
        }

        internal string Q(string name)
        {
            if (Kind == DatabaseProviderType.MySQL) return "`" + name.Replace("`", "``") + "`";
            if (Kind == DatabaseProviderType.SQLite || Kind == DatabaseProviderType.Oracle) return "\"" + name.Replace("\"", "\"\"") + "\"";
            return "[" + name.Replace("]", "]]") + "]";
        }

        internal string TableName(DatabaseObjectNode node)
        {
            if (Kind == DatabaseProviderType.Access) return Q(node.Name);
            string schema = node.SchemaName;
            if (Kind == DatabaseProviderType.MySQL && string.IsNullOrEmpty(schema)) schema = node.DatabaseName;
            if (Kind == DatabaseProviderType.SqlServer && string.IsNullOrEmpty(schema)) schema = "dbo";
            if (Kind == DatabaseProviderType.SQLite) schema = "main";
            return string.IsNullOrEmpty(schema) ? Q(node.Name) : Q(schema) + "." + Q(node.Name);
        }

        internal string BuildType(TableColumnDefinition column)
        {
            string type = column.TypeName.Trim();
            string args = column.TypeArguments.Trim();
            bool enumArguments = Kind == DatabaseProviderType.MySQL && (type.Equals("enum", StringComparison.OrdinalIgnoreCase) || type.Equals("set", StringComparison.OrdinalIgnoreCase));
            if (enumArguments)
            {
                var tokens = SqlDefinitionParser.Tokens(args);
                if (tokens.Count == 0 || tokens.Count % 2 == 0 || tokens.Where((t, i) => i % 2 == 0 ? !t.Quoted || args[t.Start] != '\'' : t.Value != ",").Any()) throw new InvalidOperationException("ENUM/SET 的可选值需要使用单引号，并以逗号分隔。");
            }
            if ((Kind != DatabaseProviderType.Access && !Regex.IsMatch(type, @"^[\p{L}_][\p{L}\p{N}_ ]*$")) || (!enumArguments && !string.IsNullOrEmpty(args) && !Regex.IsMatch(args, @"(?i)^(?:max|\d+(?:\s*,\s*-?\d+)?|\d+\s+(?:char|byte))$")))
                throw new InvalidOperationException("字段类型或长度/精度不正确：" + column.Name);
            if (Kind == DatabaseProviderType.Access)
            {
                switch (type)
                {
                    case "短文本": type = "VARCHAR"; if (args.Length == 0) args = "255"; break;
                    case "长文本": return "LONGCHAR";
                    case "字节": return "BYTE";
                    case "整型": return "SMALLINT";
                    case "长整型": return column.IsIdentity ? "COUNTER" : "INTEGER";
                    case "大整数": return "BIGINT";
                    case "单精度浮点型": return "SINGLE";
                    case "双精度浮点型": return "DOUBLE";
                    case "十进制数": type = "DECIMAL"; break;
                    case "货币": return "CURRENCY";
                    case "是/否": return "BIT";
                    case "日期/时间": return "DATETIME";
                    case "GUID": return "GUID";
                    case "二进制": type = "BINARY"; break;
                    case "OLE 对象": return "LONGBINARY";
                    default: throw new InvalidOperationException("不支持编辑该 Access 字段类型：" + type);
                }
            }
            else if (args.Length == 0 && new[] { "varchar", "nvarchar", "char", "nchar", "varchar2", "nvarchar2", "varbinary", "binary", "raw" }.Contains(type.ToLowerInvariant()))
            {
                args = "255";
            }
            if (type.IndexOf(" WITH ", StringComparison.OrdinalIgnoreCase) > 0 && args.Length > 0)
            {
                int space = type.IndexOf(' ');
                return type.Substring(0, space) + "(" + args + ")" + type.Substring(space);
            }
            // MySQL 的 unsigned 等修饰符位于类型参数后面。
            if (Kind == DatabaseProviderType.MySQL && type.IndexOf(' ') > 0 && args.Length > 0)
            {
                int space = type.IndexOf(' ');
                return type.Substring(0, space) + "(" + args + ")" + type.Substring(space);
            }
            return type + (args.Length == 0 ? string.Empty : "(" + args + ")");
        }

        internal string ColumnSql(TableColumnDefinition column, bool includeDefault = true)
        {
            string sql = Q(column.Name) + " " + BuildType(column);
            if (!string.IsNullOrEmpty(column.Collation) && (Kind == DatabaseProviderType.SqlServer || Kind == DatabaseProviderType.MySQL) &&
                Regex.IsMatch(column.TypeName, @"(?i)^(?:n?(?:var)?char|(?:tiny|medium|long)?text|enum|set)(?:\s|$)")) sql += " COLLATE " + Q(column.Collation);
            if (column.IsIdentity)
            {
                if (Kind == DatabaseProviderType.SqlServer) sql += " IDENTITY(1,1)";
                if (Kind == DatabaseProviderType.Oracle) sql += " GENERATED BY DEFAULT AS IDENTITY";
                if (Kind == DatabaseProviderType.SQLite) return sql + " PRIMARY KEY AUTOINCREMENT";
            }
            // Oracle 的 DEFAULT 必须放在 NULL/NOT NULL 前面。
            if (includeDefault && Kind == DatabaseProviderType.Oracle && column.DefaultSql.Trim().Length > 0) sql += " DEFAULT " + column.DefaultSql.Trim();
            sql += column.AllowNull ? " NULL" : " NOT NULL";
            if (includeDefault && Kind != DatabaseProviderType.Oracle && column.DefaultSql.Trim().Length > 0) sql += " DEFAULT " + column.DefaultSql.Trim();
            if (Kind == DatabaseProviderType.MySQL)
            {
                if (column.IsIdentity) sql += " AUTO_INCREMENT";
                if (!string.IsNullOrEmpty(column.Extra)) sql += " " + column.Extra;
                sql += " COMMENT " + Literal(column.Comment);
            }
            return sql;
        }

        internal string CreateTableSql(DatabaseObjectNode node, IList<TableColumnDefinition> columns)
        {
            List<string> definitions = columns.Select(c => ColumnSql(c)).ToList();
            List<TableColumnDefinition> keys = columns.Where(c => c.IsPrimaryKey).ToList();
            if (keys.Count > 0 && !(Kind == DatabaseProviderType.SQLite && keys.Any(c => c.IsIdentity)))
                definitions.Add("PRIMARY KEY (" + string.Join(", ", keys.Select(c => Q(c.Name))) + ")");
            return "CREATE TABLE " + TableName(node) + " (" + string.Join(", ", definitions) + ")";
        }

        private List<string> BuildStatements(DatabaseObjectNode node, TableSchemaSnapshot original, IList<TableColumnDefinition> columns)
        {
            List<string> sql = new List<string>();
            string table = TableName(node);
            if (original == null)
            {
                // Access 默认值通过 ADOX 设置，Jet 的 OleDb DDL 对 DEFAULT 支持不一致。
                sql.Add(CreateTableSql(node, Kind == DatabaseProviderType.Access ? WithoutDefaults(columns) : columns));
                AddComments(sql, node, columns, null);
                return sql;
            }
            if (Kind == DatabaseProviderType.MySQL) return BuildMySqlAlter(node, original, columns);
            bool keyChanged = !original.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).SequenceEqual(columns.Where(c => c.IsPrimaryKey).Select(c => c.OriginalName));
            bool keyTypeChanged = columns.Any(c => c.IsPrimaryKey && original.Columns.Any(o => o.Name == c.OriginalName && (o.TypeName != c.TypeName || o.TypeArguments != c.TypeArguments)));
            bool replaceKey = keyChanged || keyTypeChanged;
            if (replaceKey && original.Columns.Any(c => c.IsPrimaryKey))
            {
                if (string.IsNullOrEmpty(original.PrimaryKeyName)) throw new InvalidOperationException("未读取到原主键约束名称，无法安全修改主键。");
                sql.Add("ALTER TABLE " + table + " DROP CONSTRAINT " + Q(original.PrimaryKeyName));
            }
            foreach (TableColumnDefinition before in original.Columns)
            {
                TableColumnDefinition after = columns.FirstOrDefault(c => c.OriginalName == before.Name);
                if (after == null)
                {
                    DropSqlServerDefault(sql, table, before);
                    sql.Add("ALTER TABLE " + table + " DROP COLUMN " + Q(before.Name));
                    continue;
                }
                if (after.SameDefinition(before)) continue;
                bool changedType = after.TypeName != before.TypeName || after.TypeArguments != before.TypeArguments || after.AllowNull != before.AllowNull;
                bool changedDefault = after.DefaultSql != before.DefaultSql;
                if (before.IsIdentity && changedType && Kind != DatabaseProviderType.MySQL) throw new InvalidOperationException("现有自增字段不支持直接修改类型或可空属性：" + before.Name);
                if (after.Name != before.Name && Kind != DatabaseProviderType.Access)
                {
                    sql.Add(Kind == DatabaseProviderType.SqlServer
                        ? "EXEC sys.sp_rename N" + Literal(table + "." + Q(before.Name)) + ", N" + Literal(after.Name) + ", N'COLUMN'"
                        : "ALTER TABLE " + table + " RENAME COLUMN " + Q(before.Name) + " TO " + Q(after.Name));
                }
                if (changedDefault || changedType) DropSqlServerDefault(sql, table, before);
                if (changedType)
                {
                    TableColumnDefinition definition = after.Clone();
                    definition.IsIdentity = false;
                    definition.DefaultSql = string.Empty;
                    if (Kind == DatabaseProviderType.Oracle)
                    {
                        string clause = Q(after.Name) + " " + BuildType(after);
                        if (after.AllowNull != before.AllowNull) clause += after.AllowNull ? " NULL" : " NOT NULL";
                        sql.Add("ALTER TABLE " + table + " MODIFY (" + clause + ")");
                    }
                    else sql.Add("ALTER TABLE " + table + " ALTER COLUMN " + ColumnSql(definition, false));
                }
                if (Kind == DatabaseProviderType.SqlServer && (changedDefault || changedType) && !string.IsNullOrWhiteSpace(after.DefaultSql)) sql.Add("ALTER TABLE " + table + " ADD DEFAULT " + after.DefaultSql + " FOR " + Q(after.Name));
                if (Kind == DatabaseProviderType.Oracle && changedDefault) sql.Add("ALTER TABLE " + table + " MODIFY (" + Q(after.Name) + " DEFAULT " + (string.IsNullOrWhiteSpace(after.DefaultSql) ? "NULL" : after.DefaultSql) + ")");
            }
            foreach (TableColumnDefinition added in columns.Where(c => string.IsNullOrEmpty(c.OriginalName)))
            {
                TableColumnDefinition definition = added.Clone();
                if (Kind == DatabaseProviderType.Access) definition.DefaultSql = string.Empty;
                sql.Add("ALTER TABLE " + table + " ADD " + (Kind == DatabaseProviderType.Oracle ? "(" + ColumnSql(definition) + ")" : "COLUMN " + ColumnSql(definition)));
                // SQL Server 的 ADD 不包含 COLUMN 关键字。
                if (Kind == DatabaseProviderType.SqlServer) sql[sql.Count - 1] = "ALTER TABLE " + table + " ADD " + ColumnSql(definition);
            }
            if (replaceKey && columns.Any(c => c.IsPrimaryKey)) sql.Add("ALTER TABLE " + table + " ADD PRIMARY KEY (" + string.Join(", ", columns.Where(c => c.IsPrimaryKey).Select(c => Q(c.Name))) + ")");
            AddComments(sql, node, columns, original);
            return sql;
        }

        private void DropSqlServerDefault(IList<string> sql, string table, TableColumnDefinition column)
        {
            if (Kind == DatabaseProviderType.SqlServer && !string.IsNullOrEmpty(column.DefaultConstraintName)) sql.Add("ALTER TABLE " + table + " DROP CONSTRAINT " + Q(column.DefaultConstraintName));
        }

        private List<string> BuildMySqlAlter(DatabaseObjectNode node, TableSchemaSnapshot original, IList<TableColumnDefinition> columns)
        {
            List<string> clauses = new List<string>();
            bool keyChanged = !original.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).SequenceEqual(columns.Where(c => c.IsPrimaryKey).Select(c => c.OriginalName));
            if (keyChanged && original.Columns.Any(c => c.IsPrimaryKey)) clauses.Add("DROP PRIMARY KEY");
            foreach (TableColumnDefinition before in original.Columns)
            {
                TableColumnDefinition after = columns.FirstOrDefault(c => c.OriginalName == before.Name);
                if (after == null) clauses.Add("DROP COLUMN " + Q(before.Name));
                else if (!after.SameDefinition(before)) clauses.Add("CHANGE COLUMN " + Q(before.Name) + " " + ColumnSql(after));
            }
            foreach (TableColumnDefinition added in columns.Where(c => string.IsNullOrEmpty(c.OriginalName))) clauses.Add("ADD COLUMN " + ColumnSql(added));
            if (keyChanged && columns.Any(c => c.IsPrimaryKey)) clauses.Add("ADD PRIMARY KEY (" + string.Join(", ", columns.Where(c => c.IsPrimaryKey).Select(c => Q(c.Name))) + ")");
            // 同一个 ALTER 中调整主键和自增字段，避免中间状态违反 AUTO_INCREMENT 必须有索引的要求。
            return clauses.Count == 0 ? new List<string>() : new List<string> { "ALTER TABLE " + TableName(node) + " " + string.Join(", ", clauses) };
        }

        private void AddComments(IList<string> sql, DatabaseObjectNode node, IList<TableColumnDefinition> columns, TableSchemaSnapshot original)
        {
            foreach (TableColumnDefinition column in columns)
            {
                TableColumnDefinition before = original == null ? null : original.Columns.FirstOrDefault(c => c.Name == column.OriginalName);
                if ((before == null && column.Comment.Length == 0) || (before != null && column.Comment == before.Comment)) continue;
                if (Kind == DatabaseProviderType.Oracle) sql.Add("COMMENT ON COLUMN " + TableName(node) + "." + Q(column.Name) + " IS " + Literal(column.Comment));
                if (Kind == DatabaseProviderType.SqlServer)
                {
                    string procedure = before == null || !before.HasCommentProperty ? "sp_addextendedproperty" : "sp_updateextendedproperty";
                    sql.Add("EXEC sys." + procedure + " @name=N'MS_Description', @value=N" + Literal(column.Comment) + ", @level0type=N'SCHEMA', @level0name=N" + Literal(string.IsNullOrEmpty(node.SchemaName) ? "dbo" : node.SchemaName) + ", @level1type=N'TABLE', @level1name=N" + Literal(node.Name) + ", @level2type=N'COLUMN', @level2name=N" + Literal(column.Name));
                }
            }
        }

        private void SaveAccess(ConnectionProfile profile, DatabaseObjectNode node, TableSchemaSnapshot original, IList<TableColumnDefinition> columns, IList<string> statements)
        {
            object adoObject = null;
            object catalogObject = null;
            bool transactionStarted = false;
            string operation = "连接 Access";
            List<Action> restoreProperties = new List<Action>();
            try
            {
                adoObject = Activator.CreateInstance(Type.GetTypeFromProgID("ADODB.Connection", true));
                catalogObject = Activator.CreateInstance(Type.GetTypeFromProgID("ADOX.Catalog", true));
                dynamic ado = adoObject;
                dynamic catalog = catalogObject;
                ado.Open(provider.BuildConnectionString(profile));
                catalog.ActiveConnection = ado;
                if (original != null)
                {
                    // Jet/ACE 的 ALTER COLUMN NULL 不会清除 Required，必须使用 DAO。
                    // 先校验已有数据，再修改可恢复的属性；DAO 与 ADO 使用独立会话。
                    foreach (TableColumnDefinition column in columns)
                    {
                        TableColumnDefinition before = original.Columns.FirstOrDefault(c => c.Name == column.OriginalName);
                        if (before == null || before.AllowNull == column.AllowNull) continue;
                        operation = "检查字段“" + before.Name + "”的可空属性";
                        if (!column.AllowNull)
                        {
                            using (IDbConnection check = provider.CreateConnection(profile))
                            {
                                check.Open();
                                if (check.ExecuteScalar<long>("SELECT COUNT(*) FROM " + Q(node.Name) + " WHERE " + Q(before.Name) + " IS NULL") > 0)
                                    throw new InvalidOperationException("字段中已有空值，请先处理空值后再设置不允许空。");
                            }
                        }
                        operation = "设置字段“" + before.Name + "”的可空属性";
                        SetAccessRequired(profile, node.Name, before.Name, !column.AllowNull);
                        string originalName = before.Name;
                        bool required = !before.AllowNull;
                        restoreProperties.Add(() => SetAccessRequired(profile, node.Name, originalName, required));
                    }
                }
                ado.BeginTrans();
                transactionStarted = true;
                // Jet/ACE 不提供 RENAME COLUMN SQL，使用同一事务中的 ADOX 重命名。
                if (original != null)
                {
                    foreach (TableColumnDefinition column in columns.Where(c => !string.IsNullOrEmpty(c.OriginalName) && c.OriginalName != c.Name))
                    {
                        operation = "重命名字段“" + column.OriginalName + "”";
                        dynamic field = catalog.Tables[node.Name].Columns[column.OriginalName];
                        try { field.Name = column.Name; } finally { Marshal.FinalReleaseComObject(field); }
                        string oldName = column.OriginalName;
                        string newName = column.Name;
                        restoreProperties.Add(() =>
                        {
                            catalog.Tables.Refresh();
                            catalog.Tables[node.Name].Columns.Refresh();
                            dynamic renamed = catalog.Tables[node.Name].Columns[newName];
                            try { renamed.Name = oldName; } finally { Marshal.FinalReleaseComObject(renamed); }
                        });
                    }
                }
                foreach (string statement in statements) { operation = statement; ado.Execute(statement); }
                operation = "刷新表定义";
                catalog.Tables.Refresh();
                catalog.Tables[node.Name].Columns.Refresh();
                foreach (TableColumnDefinition column in columns)
                {
                    TableColumnDefinition before = original == null ? null : original.Columns.FirstOrDefault(c => c.Name == column.OriginalName);
                    bool setDefault = before == null ? column.DefaultSql.Length > 0 : before.DefaultSql != column.DefaultSql;
                    bool setComment = before == null ? column.Comment.Length > 0 : before.Comment != column.Comment;
                    if (!setDefault && !setComment) continue;
                    operation = "读取字段“" + column.Name + "”的属性";
                    dynamic field = catalog.Tables[node.Name].Columns[column.Name];
                    try
                    {
                        if (setDefault) { operation = "设置字段“" + column.Name + "”的默认值"; field.Properties["Default"].Value = column.DefaultSql.Length == 0 ? (object)DBNull.Value : column.DefaultSql; }
                        if (setComment) { operation = "设置字段“" + column.Name + "”的注释"; field.Properties["Description"].Value = column.Comment; }
                    }
                    finally { Marshal.FinalReleaseComObject(field); }
                }
                ado.CommitTrans();
                transactionStarted = false;
                restoreProperties.Clear();
            }
            catch (Exception ex)
            {
                if (transactionStarted) { try { ((dynamic)adoObject).RollbackTrans(); } catch { } }
                bool restored = true;
                foreach (Action restore in restoreProperties.AsEnumerable().Reverse()) { try { restore(); } catch { restored = false; } }
                throw new InvalidOperationException(operation + "失败：" + ex.Message + (restored ? string.Empty : "\n部分属性未能恢复，请刷新检查当前表结构。"), ex);
            }
            finally
            {
                if (catalogObject != null) Marshal.FinalReleaseComObject(catalogObject);
                if (adoObject != null) { try { ((dynamic)adoObject).Close(); } catch { } Marshal.FinalReleaseComObject(adoObject); }
            }
        }

        private void SetAccessRequired(ConnectionProfile profile, string tableName, string columnName, bool required)
        {
            System.Data.OleDb.OleDbConnectionStringBuilder builder = new System.Data.OleDb.OleDbConnectionStringBuilder(provider.BuildConnectionString(profile));
            object password;
            string connect = builder.TryGetValue("Jet OLEDB:Database Password", out password) && !string.IsNullOrEmpty(Convert.ToString(password)) ? ";PWD=" + Convert.ToString(password) : string.Empty;
            object engineObject = null;
            foreach (string name in new[] { "DAO.DBEngine.120", "DAO.DBEngine.36" })
            {
                try
                {
                    Type engineType = Type.GetTypeFromProgID(name);
                    if (engineType != null) engineObject = Activator.CreateInstance(engineType);
                    if (engineObject != null) break;
                }
                catch (COMException) { }
            }
            if (engineObject == null) throw new InvalidOperationException("修改 Access 的允许空属性需要安装与程序位数一致的 Access Database Engine（DAO）。");
            object databaseObject = null;
            object tableObject = null;
            object fieldObject = null;
            try
            {
                dynamic engine = engineObject;
                databaseObject = engine.OpenDatabase(builder.DataSource, false, false, connect);
                dynamic database = databaseObject;
                tableObject = database.TableDefs[tableName];
                fieldObject = ((dynamic)tableObject).Fields[columnName];
                ((dynamic)fieldObject).Required = required;
            }
            finally
            {
                if (fieldObject != null) Marshal.FinalReleaseComObject(fieldObject);
                if (tableObject != null) Marshal.FinalReleaseComObject(tableObject);
                if (databaseObject != null) { try { ((dynamic)databaseObject).Close(); } catch { } Marshal.FinalReleaseComObject(databaseObject); }
                Marshal.FinalReleaseComObject(engineObject);
            }
        }

        private static IList<TableColumnDefinition> WithoutDefaults(IList<TableColumnDefinition> columns)
        {
            return columns.Select(c => { TableColumnDefinition copy = c.Clone(); copy.DefaultSql = string.Empty; return copy; }).ToList();
        }

        internal static string Literal(string value) { return "'" + (value ?? string.Empty).Replace("'", "''") + "'"; }
        private static bool IsTrue(object value) { return new[] { "是", "true", "yes", "1", "y" }.Contains(Convert.ToString(value).ToLowerInvariant()); }

        private void ValidateName(string name, string description)
        {
            int max = Kind == DatabaseProviderType.Access || Kind == DatabaseProviderType.MySQL ? 64 : Kind == DatabaseProviderType.Oracle ? 128 : 128;
            if (string.IsNullOrWhiteSpace(name) || name.Length > max || name.Any(char.IsControl)) throw new InvalidOperationException(description + "不能为空，不能包含控制字符，且长度不能超过 " + max + " 个字符。");
        }

        private static void ValidateExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return;
            SqlDefinitionParser.ValidateExpression(expression);
        }
    }
}
