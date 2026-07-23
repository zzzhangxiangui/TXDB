using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TXDB.Data;
using TXDB.Models;
using TXDB.Services;

namespace TXDB.Views
{
    /// <summary>
    /// 表示一个 SQL 查询标签页，包含编辑器、结果网格、上下文选择器和导出功能。
    /// </summary>
    public partial class QueryTabView : UserControl
    {
        private DataTable resultTable;
        private SqlCompletionService pageCompletionService;
        private IList<DatabaseObjectNode> metadataRoots;
        private bool isUpdatingScopeSelection;
        private bool isUpdatingHelperSelection;
        private bool isExecuting;
        private string selectedDatabaseName;
        private string selectedSchemaName;
        private CancellationTokenSource executeCancellationSource;

        /// <summary>
        /// 请求主窗口执行 SQL 的事件。
        /// </summary>
        public event EventHandler<QueryExecuteRequestedEventArgs> ExecuteRequested;

        /// <summary>
        /// 请求主窗口保存当前 SQL 的事件。
        /// </summary>
        public event EventHandler<QuerySaveRequestedEventArgs> SaveRequested;

        /// <summary>
        /// 当前查询页绑定的连接配置。
        /// </summary>
        public ConnectionProfile Profile { get; private set; }

        /// <summary>
        /// 当前编辑器中的完整 SQL 文本。
        /// </summary>
        public string CurrentSql
        {
            get { return SqlEditor == null ? string.Empty : SqlEditor.Text; }
        }

        /// <summary>
        /// 当前查询页选择的数据库名称。
        /// </summary>
        public string SelectedDatabaseName
        {
            get { return NormalizeSelection(GetSelectedComboText(DatabaseBox)); }
        }

        /// <summary>
        /// 当前查询页选择的架构名称。
        /// </summary>
        public string SelectedSchemaName
        {
            get { return NormalizeSelection(GetSelectedComboText(SchemaBox)); }
        }

        /// <summary>
        /// 初始化查询标签页。
        /// </summary>
        public QueryTabView()
        {
            InitializeComponent();
            ResultGridHelper.Configure(ResultGrid);
            InitializeSqlHelperSelectors();
            SetExecutionState(false);
        }

        /// <summary>
        /// 初始化查询页上下文，并把当前连接的元数据和作用域传给编辑器。
        /// </summary>
        public void Initialize(ConnectionProfile profile, IEnumerable<DatabaseObjectNode> metadata, DatabaseObjectNode scopeNode)
        {
            Profile = profile == null ? null : profile.Clone();
            metadataRoots = metadata == null ? new List<DatabaseObjectNode>() : metadata.ToList();

            pageCompletionService = new SqlCompletionService();
            pageCompletionService.SetMetadata(metadataRoots);
            SqlEditor.CompletionService = pageCompletionService;

            BuildConnectionSelector();
            BuildScopeSelectors(scopeNode);
            SqlEditor.FocusEditor();
        }

        /// <summary>
        /// 主窗口工具栏调用的执行入口。
        /// </summary>
        public void RequestExecute()
        {
            OnExecuteRequested();
        }

        /// <summary>
        /// 结束当前 SQL 执行状态，恢复运行和停止按钮。
        /// </summary>
        public void EndExecution()
        {
            if (executeCancellationSource != null)
            {
                executeCancellationSource.Dispose();
                executeCancellationSource = null;
            }

            SetExecutionState(false);
        }

        /// <summary>
        /// 释放查询页持有的临时资源，关闭标签页或退出程序时调用。
        /// </summary>
        public void Cleanup()
        {
            if (executeCancellationSource != null)
            {
                executeCancellationSource.Cancel();
                executeCancellationSource.Dispose();
                executeCancellationSource = null;
            }

            SqlEditor.CloseCompletionWindow();
            SetExecutionState(false);
        }

        /// <summary>
        /// 设置执行结果并刷新结果网格。
        /// </summary>
        public void SetResult(QueryExecutionResult result)
        {
            resultTable = result == null ? new DataTable() : result.Table;
            ResultGrid.ItemsSource = resultTable == null ? null : resultTable.DefaultView;
            MessageText.Text = result == null ? "无执行结果" : result.Message + " 耗时：" + result.Duration.TotalMilliseconds.ToString("0") + " ms";
            UpdateSelectionStatus();
        }

        /// <summary>
        /// 设置查询编辑器文本。
        /// </summary>
        public void SetSql(string sql)
        {
            SqlEditor.Text = sql ?? string.Empty;
            SqlEditor.FocusEditor();
        }

        /// <summary>
        /// 设置查询页底部状态消息。
        /// </summary>
        public void SetMessage(string message)
        {
            MessageText.Text = message ?? string.Empty;
        }

        /// <summary>
        /// 主窗口工具栏调用的数据导出入口。
        /// </summary>
        public void ExportCurrentResult()
        {
            if (resultTable == null || resultTable.Columns.Count == 0)
            {
                MessageBox.Show("当前没有可导出的结果。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "导出查询结果";
            dialog.Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
            dialog.FileName = "query-result.csv";
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            ExportCsv(dialog.FileName);
            MessageText.Text = "已导出：" + dialog.FileName;
        }

        /// <summary>
        /// 处理查询页内部执行按钮。
        /// </summary>
        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            OnExecuteRequested();
        }

        /// <summary>
        /// 处理查询页内部停止按钮，首版用于请求取消并忽略后续返回结果。
        /// </summary>
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isExecuting || executeCancellationSource == null)
            {
                return;
            }

            executeCancellationSource.Cancel();
            StopButton.IsEnabled = false;
            MessageText.Text = "已请求停止，等待数据库返回。";
        }

        /// <summary>
        /// 处理 SQL 格式化按钮，把常见关键字拆成更容易阅读的结构。
        /// </summary>
        private void FormatButton_Click(object sender, RoutedEventArgs e)
        {
            SqlEditor.Text = FormatSql(SqlEditor.Text);
            SqlEditor.CloseCompletionWindow();
            SqlEditor.RefreshSemanticColoring();
            SqlEditor.FocusEditor();
            MessageText.Text = "SQL 已格式化。";
        }

        /// <summary>
        /// 保存当前查询语句到本地 JSON 文件。
        /// </summary>
        private void SaveQueryButton_Click(object sender, RoutedEventArgs e)
        {
            OnSaveRequested();
        }

        /// <summary>
        /// 处理快速插入下拉框，选中后把模板插入当前光标位置。
        /// </summary>
        private void QuickInsertBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingHelperSelection)
            {
                return;
            }

            SqlTemplateInfo item = QuickInsertBox.SelectedItem as SqlTemplateInfo;
            if (item == null)
            {
                return;
            }

            SqlEditor.InsertTextAtCaret(item.Sql);
            MessageText.Text = "已插入模板：" + item.Title;

            isUpdatingHelperSelection = true;
            try
            {
                QuickInsertBox.SelectedIndex = -1;
            }
            finally
            {
                isUpdatingHelperSelection = false;
            }
        }

        /// <summary>
        /// 处理查询页内部导出按钮。
        /// </summary>
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            ExportCurrentResult();
        }

        /// <summary>
        /// 当前单元格变化后刷新右下角选中行提示。
        /// </summary>
        private void ResultGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            UpdateSelectionStatus();
        }

        /// <summary>
        /// 选中单元格集合变化后刷新右下角选中行提示。
        /// </summary>
        private void ResultGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            UpdateSelectionStatus();
        }

        /// <summary>
        /// 连接选择框变化后刷新显示，不改变作用域。
        /// </summary>
        private void ConnectionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 这里预留给后续多连接切换，当前页只展示一个连接。
        }

        /// <summary>
        /// 数据库选择变化后，重新整理架构候选项和补全作用域。
        /// </summary>
        private void DatabaseBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingScopeSelection)
            {
                return;
            }

            BuildSchemaSelector(NormalizeSelection(GetSelectedComboText(DatabaseBox)), null);
            ApplyCompletionScope();
        }

        /// <summary>
        /// 架构选择变化后同步补全作用域。
        /// </summary>
        private void SchemaBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingScopeSelection)
            {
                return;
            }

            ApplyCompletionScope();
        }

        /// <summary>
        /// 触发执行请求事件。
        /// </summary>
        private void OnExecuteRequested()
        {
            if (isExecuting)
            {
                return;
            }

            ConnectionProfile executionProfile = BuildExecutionProfile();
            if (executionProfile == null)
            {
                return;
            }

            string sql = SqlEditor.SelectedOrAllText;
            if (string.IsNullOrWhiteSpace(sql))
            {
                MessageText.Text = "请输入需要执行的 SQL。";
                SqlEditor.FocusEditor();
                return;
            }

            executeCancellationSource = new CancellationTokenSource();
            SetExecutionState(true);
            MessageText.Text = "正在执行 SQL...";

            EventHandler<QueryExecuteRequestedEventArgs> handler = ExecuteRequested;
            if (handler != null)
            {
                handler(this, new QueryExecuteRequestedEventArgs(executionProfile, sql, executeCancellationSource.Token));
            }
            else
            {
                EndExecution();
            }
        }

        /// <summary>
        /// 触发保存查询请求事件，由主窗口决定保存名称和落盘路径。
        /// </summary>
        private void OnSaveRequested()
        {
            if (Profile == null)
            {
                MessageBox.Show("请先选择或创建一个数据库连接。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string sql = SqlEditor.Text;
            if (string.IsNullOrWhiteSpace(sql))
            {
                MessageText.Text = "请输入需要保存的 SQL。";
                SqlEditor.FocusEditor();
                return;
            }

            EventHandler<QuerySaveRequestedEventArgs> handler = SaveRequested;
            if (handler != null)
            {
                handler(this, new QuerySaveRequestedEventArgs(Profile.Clone(), SelectedDatabaseName, SelectedSchemaName, sql));
            }
        }

        /// <summary>
        /// 初始化 SQL 辅助下拉框，提供常用模板和关键字说明。
        /// </summary>
        private void InitializeSqlHelperSelectors()
        {
            isUpdatingHelperSelection = true;
            try
            {
                QuickInsertBox.ItemsSource = SqlHelperCatalog.GetTemplateItems();
                QuickInsertBox.SelectedIndex = -1;
            }
            finally
            {
                isUpdatingHelperSelection = false;
            }
        }

        /// <summary>
        /// 切换查询执行状态，并同步运行、停止和格式化按钮。
        /// </summary>
        private void SetExecutionState(bool executing)
        {
            isExecuting = executing;
            if (ExecuteButton != null)
            {
                ExecuteButton.IsEnabled = !executing;
                ExecuteButton.IsLoading = executing;
            }

            if (StopButton != null)
            {
                StopButton.IsEnabled = executing;
            }

            if (FormatButton != null)
            {
                FormatButton.IsEnabled = !executing;
            }

            if (SaveQueryButton != null)
            {
                SaveQueryButton.IsEnabled = !executing;
            }
        }

        /// <summary>
        /// 组装连接下拉框，只展示当前页所绑定的连接。
        /// </summary>
        private void BuildConnectionSelector()
        {
            isUpdatingScopeSelection = true;
            try
            {
                List<string> connectionItems = new List<string>();
                if (Profile != null)
                {
                    connectionItems.Add(BuildConnectionDisplayText(Profile));
                }
                else
                {
                    connectionItems.Add("未选择连接");
                }

                ConnectionBox.ItemsSource = connectionItems;
                ConnectionBox.SelectedIndex = 0;
            }
            finally
            {
                isUpdatingScopeSelection = false;
            }
        }

        /// <summary>
        /// 根据当前元数据和选中的节点，组装数据库与架构选择器。
        /// </summary>
        private void BuildScopeSelectors(DatabaseObjectNode scopeNode)
        {
            string preferredDatabaseName = GetPreferredDatabaseName(scopeNode);
            string preferredSchemaName = GetPreferredSchemaName(scopeNode);

            isUpdatingScopeSelection = true;
            try
            {
                List<string> databaseNames = BuildDatabaseNames();
                if (databaseNames.Count == 0)
                {
                    databaseNames.Add(GetFallbackDatabaseName());
                }

                DatabaseBox.ItemsSource = databaseNames;
                selectedDatabaseName = SelectComboItem(DatabaseBox, databaseNames, preferredDatabaseName);

                BuildSchemaSelector(selectedDatabaseName, preferredSchemaName);
            }
            finally
            {
                isUpdatingScopeSelection = false;
            }

            ApplyCompletionScope();
        }

        /// <summary>
        /// 根据当前数据库刷新架构选择器。
        /// </summary>
        private void BuildSchemaSelector(string databaseName, string preferredSchemaName)
        {
            isUpdatingScopeSelection = true;
            try
            {
                List<string> schemaNames = BuildSchemaNames(databaseName);
                if (schemaNames.Count == 0)
                {
                    string fallbackSchemaName = GetFallbackSchemaName(databaseName);
                    if (!string.IsNullOrWhiteSpace(fallbackSchemaName))
                    {
                        schemaNames.Add(fallbackSchemaName);
                    }
                }

                SchemaBox.ItemsSource = schemaNames;
                selectedSchemaName = SelectComboItem(SchemaBox, schemaNames, preferredSchemaName);
            }
            finally
            {
                isUpdatingScopeSelection = false;
            }
        }

        /// <summary>
        /// 将数据库和架构选择同步到自动提示服务。
        /// </summary>
        private void ApplyCompletionScope()
        {
            if (pageCompletionService == null)
            {
                return;
            }

            selectedDatabaseName = NormalizeSelection(GetSelectedComboText(DatabaseBox));
            selectedSchemaName = NormalizeSelection(GetSelectedComboText(SchemaBox));
            pageCompletionService.SetScope(selectedDatabaseName, selectedSchemaName);
            SqlEditor.CloseCompletionWindow();
            SqlEditor.RefreshSemanticColoring();
        }

        /// <summary>
        /// 构造执行用连接配置，数据库类型不同，目标库切换方式也不同。
        /// </summary>
        private ConnectionProfile BuildExecutionProfile()
        {
            if (Profile == null)
            {
                MessageBox.Show("请先选择或创建一个数据库连接。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            ConnectionProfile executionProfile = Profile.Clone();
            string databaseName = NormalizeSelection(GetSelectedComboText(DatabaseBox));
            if (executionProfile.ProviderType == DatabaseProviderType.SqlServer || executionProfile.ProviderType == DatabaseProviderType.MySQL)
            {
                if (!string.IsNullOrWhiteSpace(databaseName))
                {
                    executionProfile.DatabaseName = databaseName;
                }
            }

            return executionProfile;
        }

        /// <summary>
        /// 获取数据库候选项。
        /// </summary>
        private List<string> BuildDatabaseNames()
        {
            List<string> names = new List<string>();
            if (pageCompletionService != null)
            {
                names.AddRange(pageCompletionService.GetDatabaseNames());
            }

            if (names.Count == 0 && Profile != null)
            {
                if (!string.IsNullOrWhiteSpace(Profile.DatabaseName))
                {
                    names.Add(Profile.DatabaseName);
                }
                else if (Profile.ProviderType == DatabaseProviderType.SQLite)
                {
                    names.Add("main");
                }
                else if (Profile.ProviderType == DatabaseProviderType.Access && !string.IsNullOrWhiteSpace(Profile.FilePath))
                {
                    names.Add(Path.GetFileName(Profile.FilePath));
                }
            }

            return names
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item)
                .ToList();
        }

        /// <summary>
        /// 获取指定数据库下的架构候选项。
        /// </summary>
        private List<string> BuildSchemaNames(string databaseName)
        {
            List<string> names = new List<string>();
            if (pageCompletionService != null)
            {
                names.AddRange(pageCompletionService.GetSchemaNames(databaseName));
            }

            return names
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item)
                .ToList();
        }

        /// <summary>
        /// 为不同数据库类型提供默认架构名称。
        /// </summary>
        private string GetFallbackSchemaName(string databaseName)
        {
            if (Profile == null)
            {
                return string.Empty;
            }

            if (Profile.ProviderType == DatabaseProviderType.SqlServer)
            {
                return "dbo";
            }

            if (Profile.ProviderType == DatabaseProviderType.SQLite)
            {
                return "main";
            }

            if (Profile.ProviderType == DatabaseProviderType.MySQL)
            {
                return string.IsNullOrWhiteSpace(databaseName) ? Profile.DatabaseName : databaseName;
            }

            if (Profile.ProviderType == DatabaseProviderType.Oracle)
            {
                return string.IsNullOrWhiteSpace(Profile.UserName) ? string.Empty : Profile.UserName;
            }

            return string.Empty;
        }

        /// <summary>
        /// 为没有明确数据库的场景提供默认值。
        /// </summary>
        private string GetFallbackDatabaseName()
        {
            if (Profile == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(Profile.DatabaseName))
            {
                return Profile.DatabaseName;
            }

            if (Profile.ProviderType == DatabaseProviderType.SQLite)
            {
                return "main";
            }

            if (Profile.ProviderType == DatabaseProviderType.Access && !string.IsNullOrWhiteSpace(Profile.FilePath))
            {
                return Path.GetFileName(Profile.FilePath);
            }

            return string.Empty;
        }

        /// <summary>
        /// 从树节点中获取优先数据库名称。
        /// </summary>
        private string GetPreferredDatabaseName(DatabaseObjectNode scopeNode)
        {
            if (scopeNode == null)
            {
                return GetFallbackDatabaseName();
            }

            if (!string.IsNullOrWhiteSpace(scopeNode.DatabaseName))
            {
                return scopeNode.DatabaseName;
            }

            if (scopeNode.Kind == DatabaseObjectKind.Database)
            {
                return scopeNode.Name;
            }

            return GetFallbackDatabaseName();
        }

        /// <summary>
        /// 从树节点中获取优先架构名称。
        /// </summary>
        private string GetPreferredSchemaName(DatabaseObjectNode scopeNode)
        {
            if (scopeNode == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(scopeNode.SchemaName))
            {
                return scopeNode.SchemaName;
            }

            if (scopeNode.Kind == DatabaseObjectKind.Schema)
            {
                return scopeNode.Name;
            }

            return string.Empty;
        }

        /// <summary>
        /// 构造连接显示文本。
        /// </summary>
        private static string BuildConnectionDisplayText(ConnectionProfile profile)
        {
            if (profile == null)
            {
                return "未选择连接";
            }

            return profile.Name + " / " + DatabaseProviderRegistry.GetDisplayName(profile.ProviderType);
        }

        /// <summary>
        /// 选择下拉框中的指定项目。
        /// </summary>
        private static string SelectComboItem(ComboBox comboBox, IList<string> items, string preferredItem)
        {
            string selectedItem = NormalizeSelection(preferredItem);
            if (items != null && !string.IsNullOrWhiteSpace(selectedItem))
            {
                foreach (string item in items)
                {
                    if (string.Equals(item, selectedItem, StringComparison.OrdinalIgnoreCase))
                    {
                        comboBox.SelectedItem = item;
                        return item;
                    }
                }
            }

            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
                return comboBox.SelectedItem as string;
            }

            return string.Empty;
        }

        /// <summary>
        /// 安全获取当前下拉框选中的字符串。
        /// </summary>
        private static string GetSelectedComboText(ComboBox comboBox)
        {
            if (comboBox == null)
            {
                return string.Empty;
            }

            string text = comboBox.SelectedItem as string;
            return text ?? string.Empty;
        }

        /// <summary>
        /// 统一清理下拉框中的占位值。
        /// </summary>
        private static string NormalizeSelection(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "(默认)" || value == "未选择连接")
            {
                return string.Empty;
            }

            return value;
        }

        /// <summary>
        /// 对 SQL 做轻量格式化，便于临时查看和执行。
        /// </summary>
        private static string FormatSql(string sql)
        {
            string text = (sql ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            text = Regex.Replace(text, @"\s+", " ");
            text = UppercaseKnownKeywords(text);
            text = Regex.Replace(
                text,
                @"\s+(FROM|WHERE|GROUP BY|ORDER BY|HAVING|UNION ALL|UNION|LEFT JOIN|RIGHT JOIN|INNER JOIN|FULL JOIN|JOIN|ON|VALUES|SET)\b",
                "\r\n$1",
                RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s*,\s*", ", ");
            text = Regex.Replace(text, @"\r\nON\b", "\r\n    ON", RegexOptions.IgnoreCase);
            return text.Trim();
        }

        /// <summary>
        /// 将常见 SQL 关键字统一改成大写，保持字符串内容不做复杂解析。
        /// </summary>
        private static string UppercaseKnownKeywords(string sql)
        {
            string[] keywords =
            {
                "UNION ALL", "LEFT JOIN", "RIGHT JOIN", "INNER JOIN", "FULL JOIN", "ORDER BY", "GROUP BY",
                "INSERT INTO", "DELETE FROM", "SELECT", "FROM", "WHERE", "JOIN", "ON", "AND", "OR", "NOT",
                "NULL", "LIKE", "IN", "BETWEEN", "EXISTS", "DISTINCT", "HAVING", "VALUES", "UPDATE", "SET",
                "CREATE TABLE", "ALTER TABLE", "DROP TABLE", "COUNT", "SUM", "AVG", "MIN", "MAX", "AS",
                "ASC", "DESC", "LIMIT", "OFFSET", "TOP"
            };

            string text = sql ?? string.Empty;
            foreach (string keyword in keywords.OrderByDescending(item => item.Length))
            {
                string pattern = @"\b" + Regex.Escape(keyword).Replace("\\ ", "\\s+") + @"\b";
                text = Regex.Replace(text, pattern, delegate { return keyword; }, RegexOptions.IgnoreCase);
            }

            return text;
        }

        /// <summary>
        /// 将当前结果集导出为 CSV 文件。
        /// </summary>
        private void ExportCsv(string fileName)
        {
            DataView view = resultTable == null ? null : resultTable.DefaultView;
            if (view == null)
            {
                return;
            }

            using (StreamWriter writer = new StreamWriter(fileName, false, new UTF8Encoding(true)))
            {
                for (int index = 0; index < resultTable.Columns.Count; index++)
                {
                    if (index > 0)
                    {
                        writer.Write(",");
                    }

                    writer.Write(ToCsvCell(resultTable.Columns[index].ColumnName));
                }

                writer.WriteLine();

                foreach (DataRowView rowView in view)
                {
                    for (int index = 0; index < resultTable.Columns.Count; index++)
                    {
                        if (index > 0)
                        {
                            writer.Write(",");
                        }

                        writer.Write(ToCsvCell(Convert.ToString(rowView[index])));
                    }

                    writer.WriteLine();
                }
            }
        }

        /// <summary>
        /// 转义单个 CSV 单元格。
        /// </summary>
        private static string ToCsvCell(string value)
        {
            string text = value ?? string.Empty;
            if (text.Contains("\"") || text.Contains(",") || text.Contains("\r") || text.Contains("\n"))
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            return text;
        }

        /// <summary>
        /// 刷新当前结果表格选中行和总行数提示。
        /// </summary>
        private void UpdateSelectionStatus()
        {
            SelectionText.Text = ResultGridHelper.BuildSelectionStatus(ResultGrid);
            FieldText.Text = BuildCurrentFieldInfo();
        }

        /// <summary>
        /// 根据当前选中单元格构造底部字段说明。
        /// </summary>
        private string BuildCurrentFieldInfo()
        {
            string columnName = ResultGridHelper.GetCurrentColumnName(ResultGrid);
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return string.Empty;
            }

            string typeName = string.Empty;
            DataColumn column = resultTable == null || !resultTable.Columns.Contains(columnName)
                ? null
                : resultTable.Columns[columnName];
            if (column != null && column.DataType != null)
            {
                typeName = column.DataType.Name;
            }

            DatabaseObjectNode metadataColumn = FindMetadataColumn(columnName);
            if (metadataColumn != null && !string.IsNullOrWhiteSpace(metadataColumn.DataType))
            {
                typeName = metadataColumn.DataType;
            }

            string comment = metadataColumn == null ? string.Empty : metadataColumn.Comment;
            return FormatFieldInfo(columnName, typeName, comment);
        }

        /// <summary>
        /// 在当前查询页作用域内查找同名字段元数据，能匹配时补上数据库类型和注释。
        /// </summary>
        private DatabaseObjectNode FindMetadataColumn(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName) || metadataRoots == null)
            {
                return null;
            }

            foreach (DatabaseObjectNode node in FlattenMetadata(metadataRoots))
            {
                if (node.Kind == DatabaseObjectKind.Column &&
                    SameName(node.Name, columnName) &&
                    (string.IsNullOrWhiteSpace(selectedDatabaseName) || SameName(node.DatabaseName, selectedDatabaseName)) &&
                    (string.IsNullOrWhiteSpace(selectedSchemaName) || SameName(node.SchemaName, selectedSchemaName)))
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// 展平元数据树，便于从查询结果列名反查字段说明。
        /// </summary>
        private static IEnumerable<DatabaseObjectNode> FlattenMetadata(IEnumerable<DatabaseObjectNode> nodes)
        {
            if (nodes == null)
            {
                yield break;
            }

            foreach (DatabaseObjectNode node in nodes)
            {
                yield return node;
                foreach (DatabaseObjectNode child in FlattenMetadata(node.Children))
                {
                    yield return child;
                }
            }
        }

        /// <summary>
        /// 统一格式化底部字段信息文本。
        /// </summary>
        private static string FormatFieldInfo(string columnName, string typeName, string comment)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("字段：").Append(columnName);
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                builder.Append("  类型：").Append(typeName);
            }

            if (!string.IsNullOrWhiteSpace(comment))
            {
                builder.Append("  注释：").Append(comment);
            }

            return builder.ToString();
        }

        /// <summary>
        /// 使用忽略大小写方式比较名称。
        /// </summary>
        private static bool SameName(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
