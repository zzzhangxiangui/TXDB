using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TXDB.Data
{
    internal sealed class SqlDefinitionToken
    {
        public int Start;
        public int End;
        public string Value;
        public bool Identifier;
        public bool Quoted;
    }

    /// <summary>只解析结构定义边界，保留未编辑的数据库原始约束，不拆分引号内的逗号。</summary>
    internal static class SqlDefinitionParser
    {
        internal static List<SqlDefinitionToken> Tokens(string sql)
        {
            List<SqlDefinitionToken> result = new List<SqlDefinitionToken>();
            for (int i = 0; i < sql.Length;)
            {
                if (char.IsWhiteSpace(sql[i])) { i++; continue; }
                if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
                {
                    while (i < sql.Length && sql[i] != '\n') i++;
                    continue;
                }
                if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
                {
                    int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) throw new InvalidOperationException("SQL 注释未结束。");
                    i = end + 2;
                    continue;
                }
                int start = i;
                char first = sql[i++];
                bool quoted = first == '\'' || first == '"' || first == '[' || first == '`';
                bool identifier = first != '\'' && (quoted || char.IsLetter(first) || first == '_');
                string value;
                if (quoted)
                {
                    char close = first == '[' ? ']' : first;
                    StringBuilder content = new StringBuilder();
                    bool closed = false;
                    while (i < sql.Length)
                    {
                        char current = sql[i++];
                        if (current == close)
                        {
                            if (i < sql.Length && sql[i] == close) { content.Append(close); i++; }
                            else { closed = true; break; }
                        }
                        else content.Append(current);
                    }
                    if (!closed) throw new InvalidOperationException("SQL 引号未结束。");
                    value = content.ToString();
                }
                else
                {
                    if (char.IsLetterOrDigit(first) || first == '_')
                    {
                        while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '$')) i++;
                    }
                    value = sql.Substring(start, i - start);
                }
                result.Add(new SqlDefinitionToken { Start = start, End = i, Value = value, Identifier = identifier, Quoted = quoted });
            }
            return result;
        }

        internal static void ValidateExpression(string expression)
        {
            int depth = 0;
            foreach (SqlDefinitionToken token in Tokens(expression))
            {
                if (token.Quoted) continue;
                if (token.Value == ";") throw new InvalidOperationException("默认值只能填写一个 SQL 表达式，不能包含多条语句。");
                if (token.Value == "," && depth == 0) throw new InvalidOperationException("默认值中的逗号必须位于函数或表达式的括号内。");
                if (new[] { "CREATE", "ALTER", "DROP", "INSERT", "UPDATE", "DELETE", "EXEC", "EXECUTE", "GRANT", "REVOKE" }.Contains(token.Value.ToUpperInvariant())) throw new InvalidOperationException("默认值只能填写 SQL 表达式，不能包含数据库操作语句。");
                if (token.Value == "(") depth++;
                if (token.Value == ")" && --depth < 0) throw new InvalidOperationException("默认值括号不匹配。");
            }
            if (depth != 0) throw new InvalidOperationException("默认值括号不匹配。");
            // 结尾注释会吞掉后续的字段定义。
            int previousEnd = 0;
            foreach (SqlDefinitionToken token in Tokens(expression))
            {
                if (expression.Substring(previousEnd, token.Start - previousEnd).Any(c => !char.IsWhiteSpace(c))) throw new InvalidOperationException("默认值表达式不能包含 SQL 注释。");
                previousEnd = token.End;
            }
            if (expression.Substring(previousEnd).Any(c => !char.IsWhiteSpace(c))) throw new InvalidOperationException("默认值表达式不能包含 SQL 注释。");
        }

        internal static List<string> TableParts(string sql, out string suffix)
        {
            List<SqlDefinitionToken> tokens = Tokens(sql);
            int opening = tokens.FindIndex(t => !t.Quoted && t.Value == "(");
            if (opening < 0) throw new InvalidOperationException("无法读取表定义。");
            List<string> parts = new List<string>();
            int depth = 0;
            int start = tokens[opening].End;
            for (int i = opening + 1; i < tokens.Count; i++)
            {
                SqlDefinitionToken token = tokens[i];
                if (token.Quoted) continue;
                if (token.Value == "(") depth++;
                else if (token.Value == ")")
                {
                    if (depth == 0)
                    {
                        parts.Add(sql.Substring(start, token.Start - start).Trim());
                        suffix = sql.Substring(token.End).Trim().TrimEnd(';');
                        return parts;
                    }
                    depth--;
                }
                else if (token.Value == "," && depth == 0)
                {
                    parts.Add(sql.Substring(start, token.Start - start).Trim());
                    start = token.End;
                }
            }
            throw new InvalidOperationException("表定义括号不匹配。");
        }

        internal static bool ContainsIdentifier(string sql, string name)
        {
            return Tokens(sql).Any(t => t.Identifier && t.Value.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsTableConstraint(string sql)
        {
            SqlDefinitionToken first = Tokens(sql).FirstOrDefault();
            return first != null && !first.Quoted && new[] { "CONSTRAINT", "PRIMARY", "UNIQUE", "CHECK", "FOREIGN" }.Contains(first.Value.ToUpperInvariant());
        }

        // 提取字段类型与约束子句，避免修改类型时丢失 CHECK/UNIQUE/COLLATE/REFERENCES 等属性。
        internal static List<KeyValuePair<string, string>> ColumnClauses(string sql)
        {
            List<SqlDefinitionToken> tokens = Tokens(sql);
            List<int> starts = new List<int>();
            int depth = 0;
            for (int i = 1; i < tokens.Count; i++)
            {
                SqlDefinitionToken token = tokens[i];
                if (token.Quoted) continue;
                if (token.Value == "(") { depth++; continue; }
                if (token.Value == ")") { depth--; continue; }
                if (depth != 0) continue;
                string value = token.Value.ToUpperInvariant();
                string previous = i > 0 ? tokens[i - 1].Value.ToUpperInvariant() : string.Empty;
                if (value == "NOT" && (i + 1 >= tokens.Count || !tokens[i + 1].Value.Equals("NULL", StringComparison.OrdinalIgnoreCase))) continue;
                if (value == "NULL" && (previous == "NOT" || previous == "DEFAULT")) continue;
                if (value == "AS" && previous == "ALWAYS") continue;
                if (new[] { "CONSTRAINT", "PRIMARY", "NOT", "NULL", "UNIQUE", "CHECK", "DEFAULT", "COLLATE", "REFERENCES", "GENERATED", "AS" }.Contains(value))
                {
                    // CONSTRAINT 名称与紧随其后的约束视为同一个子句。
                    if (starts.Count > 0 && i >= 2 && tokens[i - 2].Value.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase)) continue;
                    starts.Add(i);
                }
            }
            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();
            int typeEnd = starts.Count == 0 ? sql.Length : tokens[starts[0]].Start;
            result.Add(new KeyValuePair<string, string>("TYPE", sql.Substring(tokens[0].End, typeEnd - tokens[0].End).Trim()));
            for (int i = 0; i < starts.Count; i++)
            {
                int tokenIndex = starts[i];
                string kind = tokens[tokenIndex].Value.ToUpperInvariant();
                if (kind == "CONSTRAINT" && tokenIndex + 2 < tokens.Count) kind = tokens[tokenIndex + 2].Value.ToUpperInvariant();
                int end = i + 1 < starts.Count ? tokens[starts[i + 1]].Start : sql.Length;
                result.Add(new KeyValuePair<string, string>(kind, sql.Substring(tokens[tokenIndex].Start, end - tokens[tokenIndex].Start).Trim()));
            }
            return result;
        }
    }
}
