using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using Dapper;
using TXDB.Models;

namespace TXDB.Data
{
    internal static class SqliteTableRebuilder
    {
        internal static void Save(IDbConnection connection, DatabaseObjectNode node, TableSchemaSnapshot original, IList<TableColumnDefinition> columns, TableSchemaEditor editor)
        {
            if (string.IsNullOrWhiteSpace(original.CreateSql) || Regex.IsMatch(original.CreateSql, @"(?i)^CREATE\s+VIRTUAL")) throw new InvalidOperationException("虚拟表暂不支持修改结构。");
            string suffix;
            List<string> parts = SqlDefinitionParser.TableParts(original.CreateSql, out suffix);
            Dictionary<string, string> rawColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> constraints = new List<string>();
            foreach (string part in parts)
            {
                if (SqlDefinitionParser.IsTableConstraint(part)) constraints.Add(part);
                else rawColumns.Add(SqlDefinitionParser.Tokens(part)[0].Value, part);
            }
            if (rawColumns.Count != original.Columns.Count) throw new InvalidOperationException("此表含隐藏或生成字段，普通表设计器不能安全重建其结构。");
            List<string> dependentSql = connection.Query<string>("SELECT sql FROM sqlite_master WHERE tbl_name=@name AND type IN ('index','trigger') AND sql IS NOT NULL", new { name = node.Name }).ToList();
            List<string> otherObjects = connection.Query<string>("SELECT sql FROM sqlite_master WHERE name<>@name AND type IN ('table','view','trigger') AND sql IS NOT NULL", new { name = node.Name }).ToList();
            bool changedKey = !original.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).SequenceEqual(columns.Where(c => c.IsPrimaryKey).Select(c => c.OriginalName));
            foreach (TableColumnDefinition before in original.Columns)
            {
                TableColumnDefinition after = columns.FirstOrDefault(c => c.OriginalName == before.Name);
                if (after != null && after.Name == before.Name) continue;
                IEnumerable<string> dependencies = constraints.Where(c => !(changedKey && IsPrimaryKeyConstraint(c)))
                    .Concat(rawColumns.Where(c => !c.Key.Equals(before.Name, StringComparison.OrdinalIgnoreCase)).Select(c => c.Value))
                    .Concat(dependentSql);
                if (dependencies.Any(s => SqlDefinitionParser.ContainsIdentifier(s, before.Name)) ||
                    otherObjects.Any(s => SqlDefinitionParser.ContainsIdentifier(s, node.Name) && SqlDefinitionParser.ContainsIdentifier(s, before.Name)))
                    throw new InvalidOperationException("字段“" + before.Name + "”被约束、索引、触发器或视图引用，不能直接删除或重命名，请先处理相关依赖。");
                if (after != null)
                {
                    List<KeyValuePair<string, string>> clauses = SqlDefinitionParser.ColumnClauses(rawColumns[before.Name]);
                    if (clauses.Skip(1).Any(c => SqlDefinitionParser.ContainsIdentifier(c.Value, before.Name))) throw new InvalidOperationException("字段“" + before.Name + "”的约束引用了自身，暂不能直接重命名。");
                }
            }
            List<string> definitions = new List<string>();
            foreach (TableColumnDefinition column in columns)
            {
                TableColumnDefinition before = original.Columns.FirstOrDefault(c => c.Name == column.OriginalName);
                if (before == null) { definitions.Add(editor.ColumnSql(column)); continue; }
                if (before.SameDefinition(column) && !changedKey) { definitions.Add(rawColumns[before.Name]); continue; }
                List<KeyValuePair<string, string>> clauses = SqlDefinitionParser.ColumnClauses(rawColumns[before.Name]);
                string type = before.TypeName == column.TypeName && before.TypeArguments == column.TypeArguments ? clauses[0].Value : editor.BuildType(column);
                List<string> kept = new List<string>();
                foreach (KeyValuePair<string, string> clause in clauses.Skip(1))
                {
                    if (changedKey && clause.Key == "PRIMARY") continue;
                    if (before.AllowNull != column.AllowNull && (clause.Key == "NOT" || clause.Key == "NULL")) continue;
                    if (before.DefaultSql != column.DefaultSql && clause.Key == "DEFAULT") continue;
                    kept.Add(clause.Value);
                }
                if (before.AllowNull != column.AllowNull && !column.AllowNull) kept.Add("NOT NULL");
                if (before.DefaultSql != column.DefaultSql && !string.IsNullOrWhiteSpace(column.DefaultSql)) kept.Add("DEFAULT " + column.DefaultSql);
                definitions.Add(editor.Q(column.Name) + " " + type + " " + string.Join(" ", kept));
            }
            definitions.AddRange(constraints.Where(c => !(changedKey && IsPrimaryKeyConstraint(c))));
            if (changedKey && columns.Any(c => c.IsPrimaryKey) && !columns.Any(c => c.IsIdentity)) definitions.Add("PRIMARY KEY (" + string.Join(", ", columns.Where(c => c.IsPrimaryKey).Select(c => editor.Q(c.Name))) + ")");
            if (changedKey && columns.Any(c => c.IsIdentity && !string.IsNullOrEmpty(c.OriginalName))) throw new InvalidOperationException("SQLite 现有自增主键暂不支持更换主键组合。");

            string temporaryName = "_txdb_design_" + Guid.NewGuid().ToString("N");
            string temporary = editor.Q("main") + "." + editor.Q(temporaryName);
            string table = editor.TableName(node);
            long foreignKeys = connection.ExecuteScalar<long>("PRAGMA foreign_keys");
            HashSet<string> previousViolations = ReadForeignKeyViolations(connection, null);
            long? oldSequence = null;
            if (original.Columns.Any(c => c.IsIdentity)) oldSequence = connection.ExecuteScalar<long?>("SELECT seq FROM sqlite_sequence WHERE name=@name", new { name = node.Name });
            connection.Execute("PRAGMA foreign_keys=OFF");
            try
            {
                using (IDbTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        connection.Execute("CREATE TABLE " + temporary + " (" + string.Join(", ", definitions) + ") " + suffix, transaction: transaction);
                        List<TableColumnDefinition> existing = columns.Where(c => !string.IsNullOrEmpty(c.OriginalName)).ToList();
                        List<string> targets = existing.Select(c => editor.Q(c.Name)).ToList();
                        List<string> sources = existing.Select(c => editor.Q(c.OriginalName)).ToList();
                        if (!Regex.IsMatch(suffix, @"(?i)WITHOUT\s+ROWID") && !original.Columns.Any(c => new[] { "rowid", "_rowid_", "oid" }.Contains(c.Name.ToLowerInvariant())) && !columns.Any(c => c.Name.Equals("rowid", StringComparison.OrdinalIgnoreCase)))
                        {
                            targets.Insert(0, "rowid");
                            sources.Insert(0, "rowid");
                        }
                        if (targets.Count == 0)
                        {
                            targets.Add(editor.Q(columns[0].Name));
                            sources.Add(string.IsNullOrWhiteSpace(columns[0].DefaultSql) ? "NULL" : columns[0].DefaultSql);
                        }
                        connection.Execute("INSERT INTO " + temporary + " (" + string.Join(", ", targets) + ") SELECT " + string.Join(", ", sources) + " FROM " + table, transaction: transaction);
                        connection.Execute("DROP TABLE " + table, transaction: transaction);
                        connection.Execute("ALTER TABLE " + temporary + " RENAME TO " + editor.Q(node.Name), transaction: transaction);
                        foreach (string sql in dependentSql) connection.Execute(sql, transaction: transaction);
                        if (oldSequence.HasValue && columns.Any(c => c.IsIdentity)) connection.Execute("UPDATE sqlite_sequence SET seq=CASE WHEN seq<@seq THEN @seq ELSE seq END WHERE name=@name", new { seq = oldSequence.Value, name = node.Name }, transaction);
                        if (ReadForeignKeyViolations(connection, transaction).Except(previousViolations).Any()) throw new InvalidOperationException("本次结构修改会破坏外键关系，已取消提交。");
                        transaction.Commit();
                    }
                    catch { transaction.Rollback(); throw; }
                }
            }
            finally { connection.Execute("PRAGMA foreign_keys=" + (foreignKeys == 0 ? "OFF" : "ON")); }
        }

        private static bool IsPrimaryKeyConstraint(string sql)
        {
            List<SqlDefinitionToken> tokens = SqlDefinitionParser.Tokens(sql);
            int index = tokens.Count > 0 && tokens[0].Value.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            return tokens.Count > index && tokens[index].Value.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> ReadForeignKeyViolations(IDbConnection connection, IDbTransaction transaction)
        {
            return new HashSet<string>(connection.Query("PRAGMA foreign_key_check", transaction: transaction).Select(row => string.Join("|", ((IDictionary<string, object>)row).Values.Select(v => Convert.ToString(v)))));
        }
    }
}
