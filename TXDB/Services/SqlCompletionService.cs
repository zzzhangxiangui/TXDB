using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TXDB.Models;

namespace TXDB.Services
{
    /// <summary>
    /// 负责根据当前 SQL 上下文返回关键字、表名和字段名提示。
    /// </summary>
    public class SqlCompletionService
    {
        private readonly List<SqlCompletionItem> staticItems;
        private readonly List<SqlCompletionItem> metadataItems;
        private readonly List<DatabaseObjectNode> metadataRoots;
        private string selectedDatabaseName;
        private string selectedSchemaName;

        /// <summary>
        /// 初始化 SQL 自动提示服务。
        /// </summary>
        public SqlCompletionService()
        {
            staticItems = BuildStaticItems();
            metadataItems = new List<SqlCompletionItem>();
            metadataRoots = new List<DatabaseObjectNode>();
            selectedDatabaseName = string.Empty;
            selectedSchemaName = string.Empty;
        }

        /// <summary>
        /// 元数据缓存变化事件，SQL 编辑器用它刷新语义着色。
        /// </summary>
        public event EventHandler MetadataChanged;

        /// <summary>
        /// 当前查询页选中的数据库名称。
        /// </summary>
        public string SelectedDatabaseName
        {
            get { return selectedDatabaseName; }
        }

        /// <summary>
        /// 当前查询页选中的架构名称。
        /// </summary>
        public string SelectedSchemaName
        {
            get { return selectedSchemaName; }
        }

        /// <summary>
        /// 将最新数据库元数据写入自动提示缓存。
        /// </summary>
        public void SetMetadata(IEnumerable<DatabaseObjectNode> nodes)
        {
            metadataRoots.Clear();
            if (nodes != null)
            {
                metadataRoots.AddRange(nodes);
            }

            RebuildMetadataItems();
            OnMetadataChanged();
        }

        /// <summary>
        /// 创建带有相同元数据但独立作用域的提示服务，避免多个查询页互相串库。
        /// </summary>
        public SqlCompletionService CreateScopedCopy(string databaseName, string schemaName)
        {
            SqlCompletionService service = new SqlCompletionService();
            service.SetMetadata(metadataRoots);
            service.SetScope(databaseName, schemaName);
            return service;
        }

        /// <summary>
        /// 返回当前缓存的元数据根节点，查询页用它生成数据库和架构下拉框。
        /// </summary>
        public IList<DatabaseObjectNode> GetMetadataRoots()
        {
            return metadataRoots.ToList();
        }

        /// <summary>
        /// 设置当前查询页的数据库和架构作用域。
        /// </summary>
        public void SetScope(string databaseName, string schemaName)
        {
            string safeDatabaseName = databaseName ?? string.Empty;
            string safeSchemaName = schemaName ?? string.Empty;
            if (SameName(selectedDatabaseName, safeDatabaseName) && SameName(selectedSchemaName, safeSchemaName))
            {
                return;
            }

            selectedDatabaseName = safeDatabaseName;
            selectedSchemaName = safeSchemaName;
            OnMetadataChanged();
        }

        /// <summary>
        /// 获取当前元数据中的数据库名称集合。
        /// </summary>
        public IList<string> GetDatabaseNames()
        {
            return metadataRoots
                .Where(item => item.Kind == DatabaseObjectKind.Database)
                .Select(item => string.IsNullOrWhiteSpace(item.Name) ? item.DatabaseName : item.Name)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item)
                .ToList();
        }

        /// <summary>
        /// 获取指定数据库下可选择的架构名称集合。
        /// </summary>
        public IList<string> GetSchemaNames(string databaseName)
        {
            List<string> schemaNames = new List<string>();
            foreach (DatabaseObjectNode databaseNode in metadataRoots.Where(item => item.Kind == DatabaseObjectKind.Database))
            {
                string currentDatabaseName = string.IsNullOrWhiteSpace(databaseNode.Name) ? databaseNode.DatabaseName : databaseNode.Name;
                if (!string.IsNullOrWhiteSpace(databaseName) && !SameName(currentDatabaseName, databaseName))
                {
                    continue;
                }

                foreach (ScopedMetadataNode scopedNode in FlattenWithScope(databaseNode.Children, currentDatabaseName, string.Empty))
                {
                    DatabaseObjectNode node = scopedNode.Node;
                    if (node.Kind == DatabaseObjectKind.Schema && !string.IsNullOrWhiteSpace(node.Name))
                    {
                        schemaNames.Add(node.Name);
                    }
                    else if ((node.Kind == DatabaseObjectKind.Table || node.Kind == DatabaseObjectKind.View || node.Kind == DatabaseObjectKind.Column) &&
                             !string.IsNullOrWhiteSpace(scopedNode.SchemaName))
                    {
                        schemaNames.Add(scopedNode.SchemaName);
                    }
                }
            }

            return schemaNames
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item)
                .ToList();
        }

        /// <summary>
        /// 触发元数据变化事件。
        /// </summary>
        private void OnMetadataChanged()
        {
            EventHandler handler = MetadataChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 根据当前文本和光标位置计算提示项。
        /// </summary>
        public IList<SqlCompletionItem> GetItems(string text, int caretOffset)
        {
            string safeText = text ?? string.Empty;
            int safeOffset = Math.Max(0, Math.Min(caretOffset, safeText.Length));
            string prefix = GetCurrentToken(safeText, safeOffset);
            int contextOffset = Math.Max(0, safeOffset - prefix.Length);
            string qualifier = GetDotQualifier(safeText, safeOffset);
            Dictionary<string, string> aliases = BuildAliasMap(safeText);
            IEnumerable<SqlCompletionItem> scopedMetadataItems = GetScopedMetadataItems();

            IEnumerable<SqlCompletionItem> candidates;
            if (!string.IsNullOrEmpty(qualifier))
            {
                string owner = aliases.ContainsKey(qualifier) ? aliases[qualifier] : qualifier;
                candidates = scopedMetadataItems.Where(item => item.Kind == SqlCompletionItemKind.Column && SameName(item.OwnerName, owner));
            }
            else if (IsAfterTableKeyword(safeText, contextOffset))
            {
                candidates = scopedMetadataItems.Where(item => item.Kind == SqlCompletionItemKind.Table);
            }
            else if (IsAfterFieldKeyword(safeText, contextOffset))
            {
                candidates = scopedMetadataItems.Where(item => item.Kind == SqlCompletionItemKind.Column).Concat(staticItems);
            }
            else
            {
                candidates = staticItems.Concat(scopedMetadataItems);
            }

            return candidates
                .Where(item => string.IsNullOrEmpty(prefix) || MatchesCompletionPrefix(item.Text, prefix))
                .GroupBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Priority).First())
                .OrderBy(item => GetMatchRank(item.Text, prefix))
                .ThenByDescending(item => item.Priority)
                .ThenBy(item => item.Text)
                .Take(80)
                .ToList();
        }

        /// <summary>
        /// 判断提示项是否与当前输入前缀匹配。
        /// </summary>
        private static bool MatchesCompletionPrefix(string text, string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return true;
            }

            return (text ?? string.Empty).IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 计算提示项与当前前缀的匹配等级。
        /// </summary>
        /// <remarks>
        /// 数字越小越靠前，前缀完全匹配优先，其次才是包含匹配。
        /// </remarks>
        private static int GetMatchRank(string text, string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return 0;
            }

            string safeText = text ?? string.Empty;
            if (safeText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return safeText.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 2;
        }

        /// <summary>
        /// 获取当前用于语法着色的 SQL 关键字集合。
        /// </summary>
        public ISet<string> GetKeywordTexts()
        {
            HashSet<string> keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SqlCompletionItem item in staticItems.Where(item => item.Kind == SqlCompletionItemKind.Keyword || item.Kind == SqlCompletionItemKind.Function))
            {
                keywords.Add(item.Text);
                foreach (string part in item.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    keywords.Add(part);
                }
            }

            return keywords;
        }

        /// <summary>
        /// 获取当前作用域内的表和视图名称集合。
        /// </summary>
        public ISet<string> GetTableTexts()
        {
            return new HashSet<string>(
                GetScopedMetadataItems().Where(item => item.Kind == SqlCompletionItemKind.Table).Select(item => item.Text),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取当前作用域内的字段名称集合。
        /// </summary>
        public ISet<string> GetColumnTexts()
        {
            return new HashSet<string>(
                GetScopedMetadataItems().Where(item => item.Kind == SqlCompletionItemKind.Column).Select(item => item.Text),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 根据原始元数据重建扁平提示项集合。
        /// </summary>
        private void RebuildMetadataItems()
        {
            metadataItems.Clear();
            foreach (ScopedMetadataNode scopedNode in FlattenWithScope(metadataRoots, string.Empty, string.Empty))
            {
                DatabaseObjectNode node = scopedNode.Node;
                if (node.Kind == DatabaseObjectKind.Database)
                {
                    metadataItems.Add(CreateItem(node.Name, "数据库", SqlCompletionItemKind.Database, string.Empty, scopedNode.DatabaseName, string.Empty, 70));
                }
                else if (node.Kind == DatabaseObjectKind.Schema)
                {
                    metadataItems.Add(CreateItem(node.Name, "架构", SqlCompletionItemKind.Schema, string.Empty, scopedNode.DatabaseName, scopedNode.SchemaName, 75));
                }
                else if (node.Kind == DatabaseObjectKind.Table || node.Kind == DatabaseObjectKind.View)
                {
                    string description = node.Kind == DatabaseObjectKind.Table ? "表" : "视图";
                    metadataItems.Add(CreateItem(node.Name, description, SqlCompletionItemKind.Table, scopedNode.SchemaName, scopedNode.DatabaseName, scopedNode.SchemaName, 90));
                }
                else if (node.Kind == DatabaseObjectKind.Column)
                {
                    string description = string.IsNullOrEmpty(node.DataType) ? "字段" : "字段：" + node.DataType;
                    metadataItems.Add(CreateItem(node.Name, description, SqlCompletionItemKind.Column, node.ParentName, scopedNode.DatabaseName, scopedNode.SchemaName, 85));
                }
            }
        }

        /// <summary>
        /// 返回当前数据库和架构作用域内允许参与提示和着色的元数据项。
        /// </summary>
        private IEnumerable<SqlCompletionItem> GetScopedMetadataItems()
        {
            foreach (SqlCompletionItem item in metadataItems)
            {
                if (MatchesDatabaseScope(item) && MatchesSchemaScope(item))
                {
                    yield return item;
                }
            }
        }

        /// <summary>
        /// 判断提示项是否属于当前数据库作用域。
        /// </summary>
        private bool MatchesDatabaseScope(SqlCompletionItem item)
        {
            if (string.IsNullOrWhiteSpace(selectedDatabaseName))
            {
                return true;
            }

            if (item.Kind == SqlCompletionItemKind.Database)
            {
                return SameName(item.Text, selectedDatabaseName) || SameName(item.DatabaseName, selectedDatabaseName);
            }

            return string.IsNullOrWhiteSpace(item.DatabaseName) || SameName(item.DatabaseName, selectedDatabaseName);
        }

        /// <summary>
        /// 判断提示项是否属于当前架构作用域。
        /// </summary>
        private bool MatchesSchemaScope(SqlCompletionItem item)
        {
            if (string.IsNullOrWhiteSpace(selectedSchemaName) || item.Kind == SqlCompletionItemKind.Database)
            {
                return true;
            }

            if (item.Kind == SqlCompletionItemKind.Schema)
            {
                return SameName(item.Text, selectedSchemaName) || SameName(item.SchemaName, selectedSchemaName);
            }

            return string.IsNullOrWhiteSpace(item.SchemaName) || SameName(item.SchemaName, selectedSchemaName);
        }

        /// <summary>
        /// 构造固定 SQL 关键字和常用函数。
        /// </summary>
        private static List<SqlCompletionItem> BuildStaticItems()
        {
            string[] keywords =
            {
                "SELECT", "FROM", "WHERE", "JOIN", "LEFT JOIN", "RIGHT JOIN", "INNER JOIN", "ORDER BY",
                "GROUP BY", "HAVING", "INSERT INTO", "UPDATE", "DELETE FROM", "CREATE TABLE", "ALTER TABLE",
                "DROP TABLE", "AND", "OR", "NOT", "NULL", "IS NULL", "IS NOT NULL", "LIKE", "IN", "BETWEEN",
                "EXISTS", "DISTINCT", "TOP", "LIMIT", "OFFSET", "AS", "ON", "SET", "VALUES"
            };

            string[] functions =
            {
                "COUNT", "SUM", "AVG", "MIN", "MAX", "GETDATE", "NOW", "DATEADD", "DATEDIFF",
                "SUBSTRING", "CONCAT", "ISNULL", "NVL", "COALESCE"
            };

            List<SqlCompletionItem> items = new List<SqlCompletionItem>();
            foreach (string keyword in keywords)
            {
                items.Add(CreateItem(keyword, "SQL 关键字", SqlCompletionItemKind.Keyword, string.Empty, string.Empty, string.Empty, 60));
            }

            foreach (string function in functions)
            {
                items.Add(CreateItem(function, "常用函数", SqlCompletionItemKind.Function, string.Empty, string.Empty, string.Empty, 58));
            }

            return items;
        }

        /// <summary>
        /// 创建统一的提示项对象。
        /// </summary>
        private static SqlCompletionItem CreateItem(string text, string description, SqlCompletionItemKind kind, string ownerName, string databaseName, string schemaName, double priority)
        {
            return new SqlCompletionItem
            {
                Text = text ?? string.Empty,
                Description = description ?? string.Empty,
                Kind = kind,
                OwnerName = ownerName ?? string.Empty,
                DatabaseName = databaseName ?? string.Empty,
                SchemaName = schemaName ?? string.Empty,
                Priority = priority
            };
        }

        /// <summary>
        /// 展平元数据树，同时沿父级继承数据库和架构名称。
        /// </summary>
        private static IEnumerable<ScopedMetadataNode> FlattenWithScope(IEnumerable<DatabaseObjectNode> nodes, string inheritedDatabaseName, string inheritedSchemaName)
        {
            if (nodes == null)
            {
                yield break;
            }

            foreach (DatabaseObjectNode node in nodes)
            {
                string currentDatabaseName = ResolveDatabaseName(node, inheritedDatabaseName);
                string currentSchemaName = ResolveSchemaName(node, inheritedSchemaName);
                yield return new ScopedMetadataNode
                {
                    Node = node,
                    DatabaseName = currentDatabaseName,
                    SchemaName = currentSchemaName
                };

                foreach (ScopedMetadataNode child in FlattenWithScope(node.Children, currentDatabaseName, currentSchemaName))
                {
                    yield return child;
                }
            }
        }

        /// <summary>
        /// 根据节点和父级上下文解析数据库名称。
        /// </summary>
        private static string ResolveDatabaseName(DatabaseObjectNode node, string inheritedDatabaseName)
        {
            if (node == null)
            {
                return inheritedDatabaseName ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(node.DatabaseName))
            {
                return node.DatabaseName;
            }

            if (node.Kind == DatabaseObjectKind.Database && !string.IsNullOrWhiteSpace(node.Name))
            {
                return node.Name;
            }

            return inheritedDatabaseName ?? string.Empty;
        }

        /// <summary>
        /// 根据节点和父级上下文解析架构名称。
        /// </summary>
        private static string ResolveSchemaName(DatabaseObjectNode node, string inheritedSchemaName)
        {
            if (node == null)
            {
                return inheritedSchemaName ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(node.SchemaName))
            {
                return node.SchemaName;
            }

            if (node.Kind == DatabaseObjectKind.Schema && !string.IsNullOrWhiteSpace(node.Name))
            {
                return node.Name;
            }

            return inheritedSchemaName ?? string.Empty;
        }

        /// <summary>
        /// 获取光标前正在输入的普通单词。
        /// </summary>
        private static string GetCurrentToken(string text, int caretOffset)
        {
            int index = caretOffset - 1;
            while (index >= 0 && IsTokenChar(text[index]))
            {
                index--;
            }

            return text.Substring(index + 1, caretOffset - index - 1);
        }

        /// <summary>
        /// 获取点号前面的表名或别名。
        /// </summary>
        private static string GetDotQualifier(string text, int caretOffset)
        {
            int dotIndex = caretOffset - 1;
            while (dotIndex >= 0 && IsTokenChar(text[dotIndex]))
            {
                dotIndex--;
            }

            if (dotIndex < 0 || text[dotIndex] != '.')
            {
                return string.Empty;
            }

            int end = dotIndex - 1;
            int start = end;
            while (start >= 0 && IsTokenChar(text[start]))
            {
                start--;
            }

            return text.Substring(start + 1, end - start);
        }

        /// <summary>
        /// 判断光标是否处在表名类关键字之后。
        /// </summary>
        private static bool IsAfterTableKeyword(string text, int caretOffset)
        {
            string previous = GetPreviousKeyword(text, caretOffset);
            return SameName(previous, "FROM") || SameName(previous, "JOIN") || SameName(previous, "UPDATE") || SameName(previous, "INTO");
        }

        /// <summary>
        /// 判断光标是否处在字段类关键字之后。
        /// </summary>
        private static bool IsAfterFieldKeyword(string text, int caretOffset)
        {
            string previous = GetPreviousKeyword(text, caretOffset);
            return SameName(previous, "WHERE") || SameName(previous, "ON") || SameName(previous, "BY") || SameName(previous, "SET") || SameName(previous, "SELECT");
        }

        /// <summary>
        /// 获取光标前最近的 SQL 单词。
        /// </summary>
        private static string GetPreviousKeyword(string text, int caretOffset)
        {
            int safeOffset = Math.Max(0, Math.Min(caretOffset, text.Length));
            string head = text.Substring(0, safeOffset);
            MatchCollection matches = Regex.Matches(head, @"[A-Za-z_][A-Za-z0-9_]*");
            if (matches.Count == 0)
            {
                return string.Empty;
            }

            return matches[matches.Count - 1].Value;
        }

        /// <summary>
        /// 从 SQL 中提取简单表别名关系。
        /// </summary>
        private static Dictionary<string, string> BuildAliasMap(string text)
        {
            Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MatchCollection matches = Regex.Matches(text ?? string.Empty, @"\b(?:FROM|JOIN)\s+([A-Za-z_][A-Za-z0-9_\.]*)\s+(?:AS\s+)?([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                string tableName = match.Groups[1].Value;
                string aliasName = match.Groups[2].Value;
                if (!aliases.ContainsKey(aliasName) && !IsReservedAlias(aliasName))
                {
                    aliases.Add(aliasName, tableName.Contains(".") ? tableName.Substring(tableName.LastIndexOf('.') + 1) : tableName);
                }
            }

            return aliases;
        }

        /// <summary>
        /// 避免把 WHERE、JOIN 这类 SQL 关键字误识别成表别名。
        /// </summary>
        private static bool IsReservedAlias(string aliasName)
        {
            string[] reserved =
            {
                "WHERE", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "FULL", "ON", "ORDER", "GROUP",
                "HAVING", "LIMIT", "TOP", "UNION", "EXCEPT", "INTERSECT"
            };
            return reserved.Any(item => SameName(item, aliasName));
        }

        /// <summary>
        /// 判断字符是否可以作为 SQL 标识符的一部分。
        /// </summary>
        private static bool IsTokenChar(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_' || value == '$';
        }

        /// <summary>
        /// 使用忽略大小写方式比较名称。
        /// </summary>
        private static bool SameName(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 扁平化元数据时携带的数据库和架构上下文。
        /// </summary>
        private class ScopedMetadataNode
        {
            /// <summary>
            /// 原始元数据节点。
            /// </summary>
            public DatabaseObjectNode Node { get; set; }

            /// <summary>
            /// 当前节点所属数据库名称。
            /// </summary>
            public string DatabaseName { get; set; }

            /// <summary>
            /// 当前节点所属架构名称。
            /// </summary>
            public string SchemaName { get; set; }
        }
    }
}
