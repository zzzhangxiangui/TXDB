using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TX.WpfUI.Controls;
using TX.WpfUI.Theming;
using TXDB.Data;
using TXDB.Models;
using TXDB.Services;
using TXDB.Views;

namespace TXDB
{
    /// <summary>
    /// 主窗口负责组织工具栏、连接树和右侧工作标签页。
    /// </summary>
    public partial class MainWindow : Window
    {
        // 左侧连接树的状态色和对象色，和 SQL 编辑器的语义色保持大体一致。
        private static readonly Brush DisconnectedBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        private static readonly Brush ConnectingBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));
        private static readonly Brush ConnectedBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
        private static readonly Brush FailedBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        private static readonly Brush DatabaseBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199));
        private static readonly Brush SchemaBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        private static readonly Brush FolderBrush = new SolidColorBrush(Color.FromRgb(234, 179, 8));
        private static readonly Brush TableBrush = new SolidColorBrush(Color.FromRgb(14, 165, 233));
        private static readonly Brush ViewBrush = new SolidColorBrush(Color.FromRgb(20, 184, 166));
        private static readonly Brush ColumnBrush = new SolidColorBrush(Color.FromRgb(124, 58, 237));
        private static readonly Brush SavedQueryBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241));
        private static readonly Brush InfoBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));

        private readonly ConnectionProfileStore profileStore;
        private readonly SavedQueryStore savedQueryStore;
        private readonly AppSettingsStore appSettingsStore;
        private readonly List<ConnectionProfile> profiles;
        private readonly List<SavedQueryInfo> savedQueries;
        private readonly SqlCompletionService completionService;
        private readonly Dictionary<string, IList<DatabaseObjectNode>> metadataCache;
        private int queryIndex;

        // 初始化主题下拉框时会触发 SelectionChanged，用该标记避免重复应用主题。
        private bool isUpdatingThemeSelection;

        // 程序关闭确认标记，避免 Shutdown 过程中重复弹出确认框。
        private bool isCloseConfirmed;

        /// <summary>
        /// 左侧连接树绑定集合。
        /// </summary>
        public ObservableCollection<DatabaseTreeNode> ConnectionNodes { get; private set; }

        /// <summary>
        /// 初始化主窗口。
        /// </summary>
        public MainWindow()
        {
            profileStore = new ConnectionProfileStore();
            savedQueryStore = new SavedQueryStore();
            appSettingsStore = new AppSettingsStore();
            profiles = new List<ConnectionProfile>();
            savedQueries = new List<SavedQueryInfo>();
            completionService = new SqlCompletionService();
            metadataCache = new Dictionary<string, IList<DatabaseObjectNode>>(StringComparer.OrdinalIgnoreCase);
            ConnectionNodes = new ObservableCollection<DatabaseTreeNode>();
            queryIndex = 1;

            InitializeComponent();
            InitializeWindowTitle();
            LoadWindowIcon();
            InitializeThemeSelector();
            DataContext = this;
            LoadSavedQueries();
            LoadProfiles();
            AddWelcomeTab();
        }

        /// <summary>
        /// 根据程序集版本设置窗口标题，版本号集中跟随 AssemblyInfo 维护。
        /// </summary>
        private void InitializeWindowTitle()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            string versionText = version == null
                ? "1.0.0.0"
                : version.ToString();
            Title = "TXDB v" + versionText;
        }

        /// <summary>
        /// 从输出目录加载程序图标，找不到时不影响主界面启动。
        /// </summary>
        private void LoadWindowIcon()
        {
            try
            {
                string iconFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logo.ico");
                if (File.Exists(iconFile))
                {
                    Icon = new BitmapImage(new Uri(iconFile, UriKind.Absolute));
                }
            }
            catch
            {
                // 图标不是核心功能，加载失败时保持系统默认图标即可。
            }
        }

        /// <summary>
        /// 关闭窗口前让用户确认，避免误关正在使用的查询工具。
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (isCloseConfirmed)
            {
                return;
            }

            foreach (TableStructureTabView structure in MainTabs.Items.OfType<TXTabItem>().Select(tab => tab.Content).OfType<TableStructureTabView>())
            {
                if (!structure.ConfirmClose()) { e.Cancel = true; return; }
            }

            MessageBoxResult result = TXMessageBox.Show(this, "确定退出 TXDB 吗？正在执行的查询会请求停止。", "退出确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            isCloseConfirmed = true;
            CleanupAllTabs();
        }

        /// <summary>
        /// 主窗口关闭后主动关闭应用，避免后台窗口或执行任务残留。
        /// </summary>
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            CleanupAllTabs();
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 初始化右上角主题切换下拉框。
        /// </summary>
        private void InitializeThemeSelector()
        {
            isUpdatingThemeSelection = true;
            ThemeBox.ItemsSource = TXThemeManager.Themes;
            ThemeBox.SelectedItem = TXThemeManager.CurrentTheme ?? TXThemeManager.Themes.FirstOrDefault();
            isUpdatingThemeSelection = false;
        }

        /// <summary>
        /// 从本地配置加载连接配置。
        /// </summary>
        private void LoadProfiles()
        {
            profiles.Clear();
            try
            {
                profiles.AddRange(profileStore.Load());
            }
            catch
            {
                StatusText.Text = "连接配置读取失败，可以重新创建连接。";
            }

            RebuildConnectionTree();
        }

        /// <summary>
        /// 从本地 JSON 查询文件加载全部保存查询。
        /// </summary>
        private void LoadSavedQueries()
        {
            savedQueries.Clear();
            try
            {
                savedQueries.AddRange(savedQueryStore.Load());
            }
            catch
            {
                StatusText.Text = "保存查询读取失败，可以继续新建查询。";
            }
        }

        /// <summary>
        /// 保存当前连接配置。
        /// </summary>
        private void SaveProfiles()
        {
            try
            {
                profileStore.Save(profiles);
            }
            catch
            {
                StatusText.Text = "连接配置保存失败。";
            }
        }

        /// <summary>
        /// 获取指定连接下保存的查询语句。
        /// </summary>
        private IList<SavedQueryInfo> GetSavedQueriesForConnection(ConnectionProfile profile)
        {
            if (profile == null)
            {
                return new List<SavedQueryInfo>();
            }

            return savedQueries
                .Where(item => SameName(item.ConnectionId, profile.Id))
                .OrderBy(item => item.DatabaseName)
                .ThenBy(item => item.SchemaName)
                .ThenBy(item => item.Title)
                .ToList();
        }

        /// <summary>
        /// 根据连接配置重建左侧树根节点。
        /// </summary>
        private void RebuildConnectionTree()
        {
            ConnectionNodes.Clear();
            foreach (ConnectionProfile profile in profiles)
            {
                ConnectionNodes.Add(CreateConnectionTreeNode(profile));
            }

            if (profiles.Count == 0)
            {
                ConnectionNodes.Add(new DatabaseTreeNode
                {
                Name = "暂无配置，请点击顶部按钮创建",
                    Kind = DatabaseObjectKind.Info,
                    DisplayType = "提示",
                    IconBrush = InfoBrush
                });
            }
        }

        /// <summary>
        /// 创建连接树根节点。
        /// </summary>
        private static DatabaseTreeNode CreateConnectionTreeNode(ConnectionProfile profile)
        {
            DatabaseTreeNode connectionNode = new DatabaseTreeNode
            {
                Name = profile.Name,
                Kind = DatabaseObjectKind.Connection,
                DisplayType = DatabaseProviderRegistry.GetDisplayName(profile.ProviderType),
                Profile = profile
            };
            SetNodeVisualState(connectionNode, false, "未连接", DisconnectedBrush, DisconnectedBrush);
            return connectionNode;
        }

        /// <summary>
        /// 添加默认欢迎标签页。
        /// </summary>
        private void AddWelcomeTab()
        {
            TXTabItem tab = new TXTabItem();
            tab.Header = "开始";
            tab.IsClosable = false;
            Border welcomePanel = new Border
            {
                Padding = new Thickness(28)
            };
            welcomePanel.SetResourceReference(Border.BackgroundProperty, "TX.Brush.Surface");

            TextBlock welcomeText = new TextBlock
            {
                Text = "新建连接后，可在左侧双击连接加载数据库列表；双击数据库加载表；双击表打开分页数据预览；右键表可查看结构；查询结果支持导出 CSV、TXT。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 16
            };
            welcomeText.SetResourceReference(TextBlock.ForegroundProperty, "TX.Brush.TextSecondary");
            welcomePanel.Child = welcomeText;
            tab.Content = welcomePanel;
            MainTabs.Items.Add(tab);
            MainTabs.SelectedItem = tab;
        }

        /// <summary>
        /// 处理顶部新建连接按钮。
        /// </summary>
        private void NewConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            OpenConnectionDialog(null);
        }

        /// <summary>
        /// 处理顶部新建查询按钮。
        /// </summary>
        private void NewQueryButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectionProfile profile = GetSelectedProfile();
            DatabaseObjectNode scopeNode = GetSelectedScopeNode();
            CreateQueryTab(profile, BuildDefaultQuerySql(profile, scopeNode), false, scopeNode);
        }

        /// <summary>
        /// 处理顶部导入连接按钮。
        /// </summary>
        private void ImportConnectionsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "导入连接";
            dialog.Filter = "JSON 连接文件 (*.json)|*.json|所有文件 (*.*)|*.*";
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                IList<ConnectionProfile> importProfiles = profileStore.ImportFromFile(dialog.FileName);
                if (importProfiles.Count == 0)
                {
                    TXMessageBox.Show(this, "导入文件中没有连接配置。", "导入连接", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int idChangedCount;
                int renamedCount;
                IList<ConnectionProfile> newProfiles = NormalizeImportedConnections(importProfiles, out idChangedCount, out renamedCount);
                if (newProfiles.Count == 0)
                {
                    TXMessageBox.Show(this, "导入文件中没有可用的连接配置。", "导入连接", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                profiles.AddRange(newProfiles);
                SaveProfiles();
                RebuildConnectionTree();

                string message = "已导入连接：" + newProfiles.Count + " 个";
                if (idChangedCount > 0 || renamedCount > 0)
                {
                    message += "，其中 " + idChangedCount + " 个重复 ID 已重新生成，" + renamedCount + " 个重名连接已自动改名。";
                }

                StatusText.Text = message;
                TXMessageBox.Show(this, message, "导入连接", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TXMessageBox.Show(this, ex.Message, "导入连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理顶部导出连接按钮。
        /// </summary>
        private void ExportConnectionsButton_Click(object sender, RoutedEventArgs e)
        {
            List<ConnectionProfile> exportableProfiles = profiles
                .Where(item => item != null && !item.IsDesignSample)
                .ToList();
            if (exportableProfiles.Count == 0)
            {
                TXMessageBox.Show(this, "当前没有可导出的连接配置。", "导出连接", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ConnectionExportDialog selectDialog = new ConnectionExportDialog(exportableProfiles);
            selectDialog.Owner = this;
            if (selectDialog.ShowDialog() != true)
            {
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "导出连接";
            dialog.Filter = "JSON 连接文件 (*.json)|*.json|所有文件 (*.*)|*.*";
            dialog.FileName = "connections_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                profileStore.ExportToFile(dialog.FileName, selectDialog.SelectedProfiles);
                StatusText.Text = "已导出连接：" + selectDialog.SelectedProfiles.Count + " 个";
            }
            catch (Exception ex)
            {
                TXMessageBox.Show(this, ex.Message, "导出连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理右上角主题切换。
        /// </summary>
        private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingThemeSelection)
            {
                return;
            }

            TXThemeInfo theme = ThemeBox.SelectedItem as TXThemeInfo;
            if (theme != null)
            {
                TXThemeManager.ApplyTheme(theme);
                appSettingsStore.SaveThemeName(theme.Name);
            }
        }

        /// <summary>
        /// 打开 SQL 关键字帮助弹窗。
        /// </summary>
        private void SqlHelpButton_Click(object sender, RoutedEventArgs e)
        {
            SqlKeywordHelpWindow window = new SqlKeywordHelpWindow();
            window.Owner = this;
            window.ShowDialog();
        }

        /// <summary>
        /// 打开连接新增或编辑弹窗。
        /// </summary>
        private void OpenConnectionDialog(ConnectionProfile profile)
        {
            ConnectionDialog dialog = profile == null ? new ConnectionDialog() : new ConnectionDialog(profile);
            dialog.Owner = this;
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (profile == null)
            {
                profiles.Add(dialog.Profile);
            }
            else
            {
                int index = profiles.FindIndex(item => item.Id == profile.Id);
                if (index >= 0)
                {
                    profiles[index] = dialog.Profile;
                }
            }

            SaveProfiles();
            UpdateSavedQueryConnectionName(dialog.Profile);
            metadataCache.Remove(dialog.Profile.Id);
            RebuildConnectionTree();
            StatusText.Text = "连接配置已保存。";
        }

        /// <summary>
        /// 连接名称变化后同步保存查询中的可读连接名。
        /// </summary>
        private void UpdateSavedQueryConnectionName(ConnectionProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            bool changed = false;
            foreach (SavedQueryInfo query in savedQueries.Where(item => SameName(item.ConnectionId, profile.Id)))
            {
                if (!SameName(query.ConnectionName, profile.Name))
                {
                    query.ConnectionName = profile.Name;
                    changed = true;
                }
            }

            if (changed)
            {
                savedQueryStore.Save(savedQueries);
            }
        }

        /// <summary>
        /// 规范化导入连接，重复 ID 会重新生成，重复名称会追加导入序号。
        /// </summary>
        private IList<ConnectionProfile> NormalizeImportedConnections(IEnumerable<ConnectionProfile> importProfiles, out int idChangedCount, out int renamedCount)
        {
            idChangedCount = 0;
            renamedCount = 0;

            List<ConnectionProfile> normalizedProfiles = new List<ConnectionProfile>();
            List<string> usedIds = profiles.Select(item => item.Id).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            List<string> usedNames = profiles.Select(item => item.Name).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();

            foreach (ConnectionProfile importProfile in importProfiles ?? new List<ConnectionProfile>())
            {
                if (importProfile == null || importProfile.IsDesignSample)
                {
                    continue;
                }

                ConnectionProfile copy = importProfile.Clone();
                copy.IsDesignSample = false;

                if (string.IsNullOrWhiteSpace(copy.Id) || ContainsName(usedIds, copy.Id))
                {
                    copy.Id = Guid.NewGuid().ToString("N");
                    idChangedCount++;
                }

                string originalName = string.IsNullOrWhiteSpace(copy.Name) ? "新建连接" : copy.Name.Trim();
                copy.Name = BuildAvailableConnectionName(originalName, usedNames);
                if (!SameName(originalName, copy.Name))
                {
                    renamedCount++;
                }

                usedIds.Add(copy.Id);
                usedNames.Add(copy.Name);
                normalizedProfiles.Add(copy);
            }

            return normalizedProfiles;
        }

        /// <summary>
        /// 根据已有连接名称生成一个可用的新名称。
        /// </summary>
        private static string BuildAvailableConnectionName(string preferredName, IList<string> usedNames)
        {
            string baseName = string.IsNullOrWhiteSpace(preferredName) ? "新建连接" : preferredName.Trim();
            if (!ContainsName(usedNames, baseName))
            {
                return baseName;
            }

            int index = 1;
            string candidate;
            do
            {
                candidate = baseName + "（导入" + index + "）";
                index++;
            }
            while (ContainsName(usedNames, candidate));

            return candidate;
        }

        /// <summary>
        /// 判断集合中是否已经包含指定文本，比较时忽略大小写。
        /// </summary>
        private static bool ContainsName(IEnumerable<string> names, string name)
        {
            return (names ?? new List<string>()).Any(item => SameName(item, name));
        }

        /// <summary>
        /// 处理连接树双击：连接加载数据库列表，数据库加载对象，中间节点展开，表打开分页预览。
        /// </summary>
        private async void ConnectionTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DatabaseTreeNode node = GetDoubleClickTreeNode(e);
            if (node == null)
            {
                return;
            }

            if (node.Kind == DatabaseObjectKind.Connection)
            {
                if (!node.IsConnected || node.Children.Count == 0)
                {
                    await LoadConnectionMetadataAsync(node);
                }
                else
                {
                    node.IsExpanded = !node.IsExpanded;
                }
            }
            else if (node.Kind == DatabaseObjectKind.Database)
            {
                if (IsDatabaseNodeLoaded(node))
                {
                    node.IsExpanded = !node.IsExpanded;
                }
                else
                {
                    await LoadDatabaseMetadataAsync(node);
                }
            }
            else if (node.Kind == DatabaseObjectKind.Schema)
            {
                node.IsExpanded = !node.IsExpanded;
            }
            else if (node.Kind == DatabaseObjectKind.Folder ||
                     node.Kind == DatabaseObjectKind.TablesFolder ||
                     node.Kind == DatabaseObjectKind.ViewsFolder ||
                     node.Kind == DatabaseObjectKind.QueriesFolder)
            {
                node.IsExpanded = !node.IsExpanded;
            }
            else if (node.Kind == DatabaseObjectKind.SavedQuery)
            {
                OpenSavedQueryNode(node);
            }
            else if (node.Kind == DatabaseObjectKind.Table || node.Kind == DatabaseObjectKind.View)
            {
                ConnectionProfile profile = GetProfileForNode(node);
                if (profile == null)
                {
                    return;
                }

                CreateTablePreviewTab(profile, node.Metadata);
            }

            e.Handled = true;
        }

        /// <summary>
        /// 右键点击时同步选中当前行。
        /// </summary>
        private void ConnectionTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            TreeViewItem item = FindParent<TreeViewItem>(source);
            if (item != null)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        /// <summary>
        /// 打开右键菜单前根据节点类型显示对应操作。
        /// </summary>
        private void ConnectionTreeContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            bool isConnection = node != null && node.Kind == DatabaseObjectKind.Connection;
            bool isDatabase = node != null && node.Kind == DatabaseObjectKind.Database;
            bool isSchema = node != null && node.Kind == DatabaseObjectKind.Schema;
            bool isTable = node != null && (node.Kind == DatabaseObjectKind.Table || node.Kind == DatabaseObjectKind.View);
            bool isQueriesFolder = node != null && node.Kind == DatabaseObjectKind.QueriesFolder;
            bool isSavedQuery = node != null && node.Kind == DatabaseObjectKind.SavedQuery;
            bool hasNode = node != null && node.Kind != DatabaseObjectKind.Info;

            SetMenuItemVisible(OpenConnectionMenuItem, isConnection);
            SetMenuItemVisible(DisconnectConnectionMenuItem, isConnection && node.IsConnected);
            SetMenuItemVisible(OpenDatabaseMenuItem, isDatabase);
            SetMenuItemVisible(CloseDatabaseMenuItem, isDatabase && IsDatabaseNodeLoaded(node));

            SetMenuItemVisible(ObjectActionSeparator, isDatabase || isSchema || isTable || isQueriesFolder || isSavedQuery);
            SetMenuItemVisible(NewQueryMenuItem, isConnection || isDatabase || isSchema || isTable || isQueriesFolder);
            SetMenuItemVisible(OpenSavedQueryMenuItem, isSavedQuery);
            SetMenuItemVisible(QueryTopRowsMenuItem, isTable);
            SetMenuItemVisible(ViewStructureMenuItem, isTable);
            SetMenuItemVisible(NewTableMenuItem, isConnection || isDatabase || isSchema || isTable || (node != null && node.Kind == DatabaseObjectKind.TablesFolder));
            SetMenuItemVisible(DropTableMenuItem, node != null && node.Kind == DatabaseObjectKind.Table);
            SetMenuItemVisible(SavedQuerySeparator, isQueriesFolder || isSavedQuery);
            SetMenuItemVisible(ImportQueriesMenuItem, isQueriesFolder);
            SetMenuItemVisible(ExportQueriesMenuItem, isQueriesFolder || isSavedQuery);
            SetMenuItemVisible(RenameSavedQueryMenuItem, isSavedQuery);
            SetMenuItemVisible(DeleteSavedQueryMenuItem, isSavedQuery);

            SetMenuItemVisible(CopySeparator, hasNode && !isConnection);
            SetMenuItemVisible(CopyNameMenuItem, hasNode && !isConnection);
            SetMenuItemVisible(CopyFullNameMenuItem, hasNode && !isConnection);

            SetMenuItemVisible(ConnectionEditSeparator, isConnection);
            SetMenuItemVisible(EditConnectionMenuItem, isConnection);
            SetMenuItemVisible(DeleteConnectionMenuItem, isConnection);
        }

        /// <summary>
        /// 右键菜单：打开或刷新连接。
        /// </summary>
        private async void OpenConnectionMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode selectedNode = GetSelectedTreeNode();
            DatabaseTreeNode connectionNode = selectedNode != null && selectedNode.Kind == DatabaseObjectKind.Connection
                ? selectedNode
                : null;
            if (connectionNode != null)
            {
                await LoadConnectionMetadataAsync(connectionNode);
            }
        }

        /// <summary>
        /// 右键菜单：打开或刷新当前数据库对象。
        /// </summary>
        private async void OpenDatabaseMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            if (node != null && node.Kind == DatabaseObjectKind.Database)
            {
                await LoadDatabaseMetadataAsync(node);
            }
        }

        /// <summary>
        /// 右键菜单：关闭当前数据库对象树。
        /// </summary>
        private void CloseDatabaseMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            if (node == null || node.Kind != DatabaseObjectKind.Database)
            {
                return;
            }

            node.Children.Clear();
            if (node.Metadata != null)
            {
                node.Metadata.Children.Clear();
            }

            SetNodeVisualState(node, false, "未打开", DisconnectedBrush, DisconnectedBrush);
            node.IsExpanded = false;
            ConnectionProfile profile = GetProfileForNode(node);
            if (profile != null && node.Metadata != null)
            {
                ReplaceDatabaseMetadata(profile, node.Metadata);
            }

            StatusText.Text = "已关闭数据库：" + node.Name;
        }

        /// <summary>
        /// 右键菜单：新建查询。
        /// </summary>
        private void NewQueryMenu_Click(object sender, RoutedEventArgs e)
        {
            ConnectionProfile profile = GetSelectedProfile();
            DatabaseObjectNode scopeNode = GetSelectedScopeNode();
            CreateQueryTab(profile, BuildDefaultQuerySql(profile, scopeNode), false, scopeNode);
        }

        /// <summary>
        /// 右键菜单：打开保存的查询。
        /// </summary>
        private void OpenSavedQueryMenu_Click(object sender, RoutedEventArgs e)
        {
            OpenSavedQueryNode(GetSelectedTreeNode());
        }

        /// <summary>
        /// 右键菜单：导入查询语句 JSON 文件到当前查询文件夹。
        /// </summary>
        private void ImportQueriesMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            if (node == null || node.Kind != DatabaseObjectKind.QueriesFolder)
            {
                return;
            }

            ConnectionProfile profile = GetProfileForNode(node);
            if (profile == null)
            {
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "导入查询语句";
            dialog.Filter = "JSON 查询文件 (*.json)|*.json|所有文件 (*.*)|*.*";
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                IList<SavedQueryInfo> importQueries = savedQueryStore.ImportFromFile(dialog.FileName);
                if (importQueries.Count == 0)
                {
                    TXMessageBox.Show(this, "导入文件中没有查询语句。", "导入查询", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string databaseName = GetNodeDatabaseName(node, profile);
                string schemaName = GetNodeSchemaName(node, profile);
                int savedCount = 0;
                foreach (SavedQueryInfo importQuery in importQueries)
                {
                    if (importQuery == null || string.IsNullOrWhiteSpace(importQuery.Sql))
                    {
                        continue;
                    }

                    SavedQueryInfo newQuery = new SavedQueryInfo
                    {
                        ConnectionId = profile.Id,
                        ConnectionName = profile.Name,
                        DatabaseName = databaseName,
                        SchemaName = schemaName,
                        Title = BuildAvailableSavedQueryTitle(profile.Id, databaseName, schemaName, importQuery.Title, null),
                        Sql = importQuery.Sql ?? string.Empty
                    };
                    savedQueryStore.SaveOrUpdate(savedQueries, newQuery);
                    savedCount++;
                }

                if (savedCount == 0)
                {
                    TXMessageBox.Show(this, "导入文件中没有可用的 SQL 内容。", "导入查询", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                RefreshSavedQueryFoldersForConnection(profile.Id);
                node.IsExpanded = true;
                StatusText.Text = "已导入查询语句：" + savedCount + " 条";
            }
            catch (Exception ex)
            {
                TXMessageBox.Show(this, ex.Message, "导入查询失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 右键菜单：导出当前查询文件夹或单条保存查询。
        /// </summary>
        private void ExportQueriesMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            if (node == null)
            {
                return;
            }

            IList<SavedQueryInfo> exportQueries = GetSavedQueriesForNode(node);
            if (exportQueries.Count == 0)
            {
                TXMessageBox.Show(this, "当前节点没有可导出的查询语句。", "导出查询", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "导出查询语句";
            dialog.Filter = "JSON 查询文件 (*.json)|*.json|所有文件 (*.*)|*.*";
            dialog.FileName = BuildSavedQueryExportFileName(node);
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                savedQueryStore.ExportToFile(dialog.FileName, exportQueries);
                StatusText.Text = "已导出查询语句：" + exportQueries.Count + " 条";
            }
            catch (Exception ex)
            {
                TXMessageBox.Show(this, ex.Message, "导出查询失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 右键菜单：删除保存的查询。
        /// </summary>
        private void DeleteSavedQueryMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            if (node == null || node.Kind != DatabaseObjectKind.SavedQuery || node.SavedQuery == null)
            {
                return;
            }

            MessageBoxResult result = TXMessageBox.Show(this, "确定删除查询“" + node.SavedQuery.Title + "”吗？", "删除查询", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            string connectionId = node.SavedQuery.ConnectionId;
            savedQueryStore.DeleteById(savedQueries, node.SavedQuery.Id);
            RefreshSavedQueryFoldersForConnection(connectionId);
            StatusText.Text = "查询已删除。";
        }

        /// <summary>
        /// 右键菜单：重命名保存的查询。
        /// </summary>
        private void RenameSavedQueryMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            if (node == null || node.Kind != DatabaseObjectKind.SavedQuery || node.SavedQuery == null)
            {
                return;
            }

            ConnectionProfile profile = GetProfileForNode(node) ?? profiles.FirstOrDefault(item => SameName(item.Id, node.SavedQuery.ConnectionId));
            if (profile == null)
            {
                TXMessageBox.Show(this, "保存查询所属连接不存在。", "重命名查询", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string databaseName = NormalizeSavedQueryDatabaseName(profile, node.SavedQuery.DatabaseName);
            string schemaName = NormalizeSavedQuerySchemaName(profile, node.SavedQuery.SchemaName);
            SaveQueryDialog dialog = new SaveQueryDialog(
                node.SavedQuery.Title,
                "重命名位置：" + profile.Name + " / " + databaseName + " / " + schemaName,
                "重命名查询",
                "重命名保存的查询语句",
                "重命名");
            dialog.Owner = this;
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string newTitle = dialog.QueryTitle;
            if (SameName(newTitle, node.SavedQuery.Title))
            {
                StatusText.Text = "查询名称未变化。";
                return;
            }

            if (SavedQueryTitleExists(profile.Id, databaseName, schemaName, newTitle, node.SavedQuery.Id))
            {
                TXMessageBox.Show(this, "同一连接、数据库和架构下已经存在名称为“" + newTitle + "”的查询。", "重命名查询", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            node.SavedQuery.Title = newTitle;
            node.SavedQuery.UpdatedAt = DateTime.Now;
            savedQueryStore.Save(savedQueries);
            RefreshSavedQueryFoldersForConnection(profile.Id);
            UpdateTabHeaderByKey(BuildSavedQueryTabKey(node.SavedQuery), newTitle);
            StatusText.Text = "查询已重命名：" + newTitle;
        }

        /// <summary>
        /// 右键菜单：查询表前 100 行。
        /// </summary>
        private void QueryTopRowsMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            if (node == null || (node.Kind != DatabaseObjectKind.Table && node.Kind != DatabaseObjectKind.View))
            {
                return;
            }

            ConnectionProfile profile = GetProfileForNode(node);
            if (profile == null)
            {
                return;
            }

            CreateTablePreviewTab(profile, node.Metadata);
        }

        /// <summary>
        /// 右键菜单：查看表结构。
        /// </summary>
        private async void ViewStructureMenu_Click(object sender, RoutedEventArgs e)
        {
            await OpenStructureTabAsync(GetSelectedTreeNode());
        }

        private async void NewTableMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode selected = GetSelectedTreeNode();
            ConnectionProfile profile = GetProfileForNode(selected);
            if (profile == null) return;
            DatabaseTreeNode connection = GetConnectionNode(selected);
            DatabaseTreeNode database = GetDatabaseNode(selected);
            if (database == null && connection != null)
            {
                var databases = connection.Children.Where(n => n.Kind == DatabaseObjectKind.Database).ToList();
                if (databases.Count == 1) database = databases[0];
            }
            string databaseName = database != null ? database.Name : profile.DatabaseName;
            string schema = GetSchemaNode(selected) == null ? string.Empty : GetSchemaNode(selected).Name;
            if (profile.ProviderType == DatabaseProviderType.SqlServer && schema.Length == 0) schema = "dbo";
            if (profile.ProviderType == DatabaseProviderType.Oracle && schema.Length == 0) schema = (profile.UserName ?? string.Empty).ToUpperInvariant();
            if (profile.ProviderType == DatabaseProviderType.MySQL) schema = string.Empty;
            if (profile.ProviderType == DatabaseProviderType.SQLite) { schema = "main"; if (string.IsNullOrEmpty(databaseName)) databaseName = "main"; }
            if (profile.ProviderType == DatabaseProviderType.Access && string.IsNullOrEmpty(databaseName)) databaseName = Path.GetFileName(profile.FilePath);
            DatabaseObjectNode node = new DatabaseObjectNode { Name = string.Empty, DatabaseName = databaseName, SchemaName = schema, Kind = DatabaseObjectKind.Table };
            await CreateStructureViewAsync(profile, node, true);
        }

        private async void DropTableMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode selected = GetSelectedTreeNode();
            if (selected == null || selected.Kind != DatabaseObjectKind.Table) return;
            ConnectionProfile profile = GetProfileForNode(selected);
            DatabaseObjectNode node = selected.Metadata;
            if (profile == null || node == null || !CanChangeTableStructure(profile, node)) return;
            TableStructureTabView structure = FindTabByKey(BuildStructureTabKey(profile, node))?.Content as TableStructureTabView;
            if (structure != null && !structure.ConfirmClose()) return;
            string location = (profile.Name ?? string.Empty) + " / " + node.DatabaseName + " / " + (string.IsNullOrEmpty(node.SchemaName) ? node.Name : node.SchemaName + "." + node.Name);
            if (TXMessageBox.Show(this, "确定删除表吗？\n\n" + location + "\n\n表结构和全部数据都会被删除，此操作不能通过取消按钮恢复。", "删除表", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (!CanChangeTableStructure(profile, node)) return;
            CloseObjectTab(BuildStructureTabKey(profile, node));
            CloseObjectTab(BuildTablePreviewTabKey(profile, node));
            DropTableMenuItem.IsEnabled = false;
            try
            {
                TableSchemaEditor editor = new TableSchemaEditor(DatabaseProviderRegistry.GetProvider(profile.ProviderType));
                await Task.Run(() => editor.DropTable(profile, node));
                CloseObjectTab(BuildStructureTabKey(profile, node));
                CloseObjectTab(BuildTablePreviewTabKey(profile, node));
                await RefreshSchemaTreeAsync(profile, node);
                StatusText.Text = "已删除表：" + node.Name;
            }
            catch (Exception ex) { TXMessageBox.Show(this, ex.Message, "删除表失败", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { DropTableMenuItem.IsEnabled = true; }
        }

        private bool CanChangeTableStructure(ConnectionProfile profile, DatabaseObjectNode node)
        {
            TablePreviewTabView preview = FindTabByKey(BuildTablePreviewTabKey(profile, node))?.Content as TablePreviewTabView;
            if (preview != null && (preview.HasUncommittedChanges || preview.IsDataOperationInProgress))
            {
                TXMessageBox.Show(this, "表数据页有未提交的更改，或正在执行数据操作。请先完成、提交或取消，再修改表结构。", "表结构", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            TableStructureTabView structure = FindTabByKey(BuildStructureTabKey(profile, node))?.Content as TableStructureTabView;
            if (structure != null && structure.IsBusy)
            {
                TXMessageBox.Show(this, "正在处理此表的结构，请完成后再操作。", "表结构", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            return true;
        }

        private void CloseObjectTab(string key)
        {
            TXTabItem tab = FindTabByKey(key);
            if (tab == null) return;
            CleanupTab(tab);
            MainTabs.Items.Remove(tab);
        }

        private async Task RefreshSchemaTreeAsync(ConnectionProfile profile, DatabaseObjectNode node)
        {
            DatabaseTreeNode connection = ConnectionNodes.FirstOrDefault(n => n.Profile != null && n.Profile.Id == profile.Id);
            if (connection == null) return;
            DatabaseTreeNode database = connection.Children.FirstOrDefault(n => n.Kind == DatabaseObjectKind.Database && SameName(n.Name, node.DatabaseName));
            if (database == null)
            {
                await LoadConnectionMetadataAsync(connection);
                database = connection.Children.FirstOrDefault(n => n.Kind == DatabaseObjectKind.Database && SameName(n.Name, node.DatabaseName));
            }
            if (database != null) await LoadDatabaseMetadataAsync(database);
        }

        private async void StructureView_SchemaSaved(object sender, EventArgs e)
        {
            TableStructureTabView view = sender as TableStructureTabView;
            if (view == null) return;
            TXTabItem tab = MainTabs.Items.OfType<TXTabItem>().FirstOrDefault(t => t.Content == view);
            if (tab != null) { tab.Tag = BuildStructureTabKey(view.Profile, view.TableNode); tab.Header = "结构 - " + view.TableNode.Name; }
            CloseObjectTab(BuildTablePreviewTabKey(view.Profile, view.TableNode));
            await RefreshSchemaTreeAsync(view.Profile, view.TableNode);
            StatusText.Text = "表结构已提交：" + view.TableNode.Name;
        }

        private void StructureView_SchemaSaving(object sender, EventArgs e)
        {
            TableStructureTabView view = sender as TableStructureTabView;
            if (view != null && !view.IsNew) CloseObjectTab(BuildTablePreviewTabKey(view.Profile, view.TableNode));
        }

        /// <summary>
        /// 右键菜单：编辑连接。
        /// </summary>
        private void EditConnectionMenu_Click(object sender, RoutedEventArgs e)
        {
            ConnectionProfile profile = GetSelectedProfile();
            if (profile != null)
            {
                OpenConnectionDialog(profile);
            }
        }

        /// <summary>
        /// 右键菜单：删除连接。
        /// </summary>
        private void DeleteConnectionMenu_Click(object sender, RoutedEventArgs e)
        {
            ConnectionProfile profile = GetSelectedProfile();
            if (profile == null)
            {
                return;
            }

            MessageBoxResult result = TXMessageBox.Show(this, "确定删除连接“" + profile.Name + "”吗？", "删除连接", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            profiles.RemoveAll(item => item.Id == profile.Id);
            metadataCache.Remove(profile.Id);
            savedQueryStore.DeleteByConnection(savedQueries, profile.Id);
            SaveProfiles();
            RebuildConnectionTree();
            StatusText.Text = "连接和保存查询已删除。";
        }

        /// <summary>
        /// 右键菜单：关闭连接并清空已加载对象。
        /// </summary>
        private void DisconnectMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode connectionNode = GetConnectionNode(GetSelectedTreeNode());
            if (connectionNode == null)
            {
                return;
            }

            connectionNode.Children.Clear();
            SetNodeVisualState(connectionNode, false, "未连接", DisconnectedBrush, DisconnectedBrush);
            if (connectionNode.Profile != null)
            {
                metadataCache.Remove(connectionNode.Profile.Id);
            }
            StatusText.Text = "已断开：" + connectionNode.Name;
        }

        /// <summary>
        /// 右键菜单：复制当前节点名称。
        /// </summary>
        private void CopyNameMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            if (node == null)
            {
                return;
            }

            Clipboard.SetText(GetNodeDisplayName(node));
            StatusText.Text = "已复制名称：" + GetNodeDisplayName(node);
        }

        /// <summary>
        /// 右键菜单：复制当前节点完整名称。
        /// </summary>
        private void CopyFullNameMenu_Click(object sender, RoutedEventArgs e)
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            if (node == null)
            {
                return;
            }

            string fullName = GetNodeFullName(node);
            Clipboard.SetText(fullName);
            StatusText.Text = "已复制完整名称：" + fullName;
        }

        /// <summary>
        /// 异步加载连接下的数据库列表，不主动读取每个数据库的表。
        /// </summary>
        private async Task LoadConnectionMetadataAsync(DatabaseTreeNode connectionNode)
        {
            if (connectionNode == null || connectionNode.Profile == null)
            {
                return;
            }

            StatusText.Text = "正在加载数据库列表：" + connectionNode.Name;
            connectionNode.Children.Clear();
            SetNodeVisualState(connectionNode, false, "加载中", ConnectingBrush, ConnectingBrush);

            try
            {
                IDatabaseProvider provider = DatabaseProviderRegistry.GetProvider(connectionNode.Profile.ProviderType);
                IDatabaseMetadataProvider metadataProvider = provider as IDatabaseMetadataProvider;
                IList<DatabaseObjectNode> metadata = await Task.Run(() => metadataProvider.LoadMetadata(connectionNode.Profile));

                BuildConnectionObjectTree(connectionNode, metadata, new List<SavedQueryInfo>());
                metadataCache[connectionNode.Profile.Id] = metadata;
                completionService.SetMetadata(metadata);
                connectionNode.IsExpanded = true;
                SetNodeVisualState(connectionNode, true, "已连接", ConnectedBrush, ConnectedBrush);
                StatusText.Text = "已加载数据库列表：" + connectionNode.Name;
            }
            catch (Exception ex)
            {
                connectionNode.Children.Add(new DatabaseTreeNode
                {
                    Name = ex.Message,
                    Kind = DatabaseObjectKind.Info,
                    DisplayType = "错误",
                    Parent = connectionNode,
                    IconBrush = FailedBrush
                });
                connectionNode.IsExpanded = true;
                SetNodeVisualState(connectionNode, false, "失败", FailedBrush, FailedBrush);
                TXMessageBox.Show(this, ex.Message, "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 异步加载指定数据库下的表和视图元数据。
        /// </summary>
        private async Task LoadDatabaseMetadataAsync(DatabaseTreeNode databaseNode)
        {
            if (databaseNode == null || databaseNode.Kind != DatabaseObjectKind.Database)
            {
                return;
            }

            ConnectionProfile profile = GetProfileForNode(databaseNode);
            DatabaseTreeNode connectionNode = GetConnectionNode(databaseNode);
            if (profile == null || connectionNode == null)
            {
                return;
            }

            string databaseName = databaseNode.Metadata == null || string.IsNullOrWhiteSpace(databaseNode.Metadata.Name)
                ? databaseNode.Name
                : databaseNode.Metadata.Name;

            StatusText.Text = "正在打开数据库：" + databaseName;
            databaseNode.Children.Clear();
            SetNodeVisualState(databaseNode, false, "加载中", ConnectingBrush, ConnectingBrush);

            try
            {
                IDatabaseProvider provider = DatabaseProviderRegistry.GetProvider(profile.ProviderType);
                IDatabaseMetadataProvider metadataProvider = provider as IDatabaseMetadataProvider;
                ConnectionProfile loadProfile = profile.Clone();
                if ((profile.ProviderType == DatabaseProviderType.SqlServer || profile.ProviderType == DatabaseProviderType.MySQL) &&
                    !string.IsNullOrWhiteSpace(databaseName))
                {
                    loadProfile.DatabaseName = databaseName;
                }

                DatabaseObjectNode metadata = await Task.Run(() => metadataProvider.LoadDatabaseMetadata(loadProfile, databaseName));
                DatabaseTreeNode loadedNode = CreateDatabaseNode(metadata, connectionNode, profile, GetSavedQueriesForConnection(profile), true);
                loadedNode.IsExpanded = true;
                int index = connectionNode.Children.IndexOf(databaseNode);
                if (index >= 0)
                {
                    connectionNode.Children[index] = loadedNode;
                }

                ReplaceDatabaseMetadata(profile, metadata);
                connectionNode.IsConnected = true;
                SetNodeVisualState(connectionNode, true, "已连接", ConnectedBrush, ConnectedBrush);
                StatusText.Text = "已打开数据库：" + databaseName;
            }
            catch (Exception ex)
            {
                databaseNode.Children.Add(CreateInfoTreeNode(ex.Message, databaseNode, "错误"));
                SetNodeVisualState(databaseNode, false, "失败", FailedBrush, FailedBrush);
                databaseNode.IsExpanded = true;
                TXMessageBox.Show(this, ex.Message, "打开数据库失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 按 Navicat 风格把元数据整理成“连接 -> 数据库 -> 架构 -> 表/查询”的界面树。
        /// </summary>
        private static void BuildConnectionObjectTree(DatabaseTreeNode connectionNode, IList<DatabaseObjectNode> metadata, IEnumerable<SavedQueryInfo> savedQueryItems)
        {
            List<DatabaseObjectNode> metadataRoots = metadata == null ? new List<DatabaseObjectNode>() : metadata.ToList();
            if (metadataRoots.Count == 0)
            {
                AddInfoNodes(connectionNode, metadataRoots);
                return;
            }

            foreach (DatabaseObjectNode metadataRoot in metadataRoots)
            {
                if (metadataRoot.Kind == DatabaseObjectKind.Database)
                {
                    connectionNode.Children.Add(CreateDatabaseNode(metadataRoot, connectionNode, connectionNode.Profile, savedQueryItems, false));
                    continue;
                }

                if (metadataRoot.Kind == DatabaseObjectKind.Info)
                {
                    connectionNode.Children.Add(CreateInfoTreeNode(metadataRoot.Name, connectionNode, "提示"));
                }
            }

            if (connectionNode.Children.Count == 0)
            {
                AddInfoNodes(connectionNode, metadataRoots);
            }
        }

        /// <summary>
        /// 当驱动暂未提供结构化元数据时，把提示节点原样挂到连接下面。
        /// </summary>
        private static void AddInfoNodes(DatabaseTreeNode connectionNode, IEnumerable<DatabaseObjectNode> allNodes)
        {
            List<DatabaseObjectNode> infoNodes = allNodes.Where(item => item.Kind == DatabaseObjectKind.Info).ToList();
            if (infoNodes.Count == 0)
            {
                connectionNode.Children.Add(new DatabaseTreeNode
                {
                    Name = "当前连接没有读取到表或视图。",
                    Kind = DatabaseObjectKind.Info,
                    DisplayType = "提示",
                    Parent = connectionNode,
                    IconBrush = InfoBrush
                });
                return;
            }

            foreach (DatabaseObjectNode infoNode in infoNodes)
            {
                DatabaseTreeNode treeNode = CreateInfoTreeNode(infoNode.Name, connectionNode, "提示");
                treeNode.Metadata = infoNode;
                connectionNode.Children.Add(treeNode);
            }
        }

        /// <summary>
        /// 创建数据库节点，数据库下展示架构，架构下再展示“表”和“查询”文件夹。
        /// </summary>
        private static DatabaseTreeNode CreateDatabaseNode(DatabaseObjectNode databaseMetadata, DatabaseTreeNode parent, ConnectionProfile profile, IEnumerable<SavedQueryInfo> savedQueryItems, bool isOpened)
        {
            List<SavedQueryInfo> queries = savedQueryItems == null ? new List<SavedQueryInfo>() : savedQueryItems.ToList();
            string databaseName = databaseMetadata == null || string.IsNullOrWhiteSpace(databaseMetadata.Name)
                ? (databaseMetadata == null ? string.Empty : databaseMetadata.DatabaseName)
                : databaseMetadata.Name;
            IEnumerable<DatabaseObjectNode> metadataChildren = databaseMetadata == null
                ? Enumerable.Empty<DatabaseObjectNode>()
                : (IEnumerable<DatabaseObjectNode>)databaseMetadata.Children;
            List<DatabaseObjectNode> tableNodes = FlattenMetadata(metadataChildren)
                .Where(item => item.Kind == DatabaseObjectKind.Table || item.Kind == DatabaseObjectKind.View)
                .OrderBy(item => item.Kind == DatabaseObjectKind.View)
                .ThenBy(item => item.Name)
                .ToList();
            List<SavedQueryInfo> databaseQueries = queries
                .Where(item => SameName(NormalizeSavedQueryDatabaseName(profile, item.DatabaseName), NormalizeSavedQueryDatabaseName(profile, databaseName)))
                .ToList();
            List<DatabaseObjectNode> infoNodes = FlattenMetadata(metadataChildren)
                .Where(item => item.Kind == DatabaseObjectKind.Info)
                .ToList();

            DatabaseTreeNode databaseNode = new DatabaseTreeNode
            {
                Name = databaseName,
                Kind = DatabaseObjectKind.Database,
                DisplayType = "数据库",
                Metadata = databaseMetadata,
                Parent = parent
            };

            if (!isOpened)
            {
                SetNodeVisualState(databaseNode, false, "未打开", DisconnectedBrush, DisconnectedBrush);
                return databaseNode;
            }

            string openStatusText = tableNodes.Count > 0
                ? tableNodes.Count + " 表"
                : (infoNodes.Count > 0 ? "0 表" : (databaseQueries.Count > 0 ? databaseQueries.Count + " 查询" : "已打开"));
            SetNodeVisualState(databaseNode, true, openStatusText, DatabaseBrush, DatabaseBrush);

            if (tableNodes.Count == 0 && databaseQueries.Count == 0)
            {
                if (infoNodes.Count > 0)
                {
                    foreach (DatabaseObjectNode infoNode in infoNodes)
                    {
                        databaseNode.Children.Add(CreateInfoTreeNode(infoNode.Name, databaseNode, "提示"));
                    }
                }
                else
                {
                    databaseNode.StatusText = "已打开";
                }

                return databaseNode;
            }

            List<string> schemaNames = tableNodes
                .Select(item => NormalizeSchemaName(profile, item.SchemaName))
                .Concat(databaseQueries.Select(item => NormalizeSavedQuerySchemaName(profile, item.SchemaName)))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item)
                .ToList();

            foreach (string schemaName in schemaNames)
            {
                List<DatabaseObjectNode> schemaTables = tableNodes.Where(item => SameName(NormalizeSchemaName(profile, item.SchemaName), schemaName)).ToList();
                List<SavedQueryInfo> schemaQueries = databaseQueries.Where(item => SameName(NormalizeSavedQuerySchemaName(profile, item.SchemaName), schemaName)).ToList();
                DatabaseTreeNode schemaNode = CreateSchemaTreeNode(databaseMetadata, databaseNode, schemaName, schemaTables.Count);
                AddObjectFolderNodes(schemaNode, schemaTables, schemaQueries);
                databaseNode.Children.Add(schemaNode);
            }

            return databaseNode;
        }

        /// <summary>
        /// 创建架构节点，SQL Server 通常为 dbo，SQLite 通常为 main。
        /// </summary>
        private static DatabaseTreeNode CreateSchemaTreeNode(DatabaseObjectNode databaseMetadata, DatabaseTreeNode parent, string schemaName, int tableCount)
        {
            string databaseName = databaseMetadata == null || string.IsNullOrWhiteSpace(databaseMetadata.Name)
                ? (databaseMetadata == null ? string.Empty : databaseMetadata.DatabaseName)
                : databaseMetadata.Name;

            DatabaseTreeNode schemaNode = new DatabaseTreeNode
            {
                Name = schemaName,
                Kind = DatabaseObjectKind.Schema,
                DisplayType = "架构",
                Metadata = new DatabaseObjectNode
                {
                    Name = schemaName,
                    Kind = DatabaseObjectKind.Schema,
                    DatabaseName = databaseName,
                    SchemaName = schemaName
                },
                Parent = parent,
                StatusText = tableCount + " 表"
            };
            SetNodeVisualState(schemaNode, true, tableCount + " 表", SchemaBrush, SchemaBrush);
            return schemaNode;
        }

        /// <summary>
        /// 在架构节点下补齐“表”和“查询”两层，查询节点预留给后续保存复杂 SQL。
        /// </summary>
        private static void AddObjectFolderNodes(DatabaseTreeNode schemaNode, IList<DatabaseObjectNode> tableNodes, IList<SavedQueryInfo> savedQueryItems)
        {
            List<DatabaseObjectNode> orderedTables = tableNodes == null
                ? new List<DatabaseObjectNode>()
                : tableNodes.OrderBy(item => item.Kind == DatabaseObjectKind.View).ThenBy(item => item.Name).ToList();

            DatabaseTreeNode tablesFolder = CreateFolderTreeNode("表", DatabaseObjectKind.TablesFolder, schemaNode, orderedTables.Count + " 项");
            foreach (DatabaseObjectNode tableMetadata in orderedTables)
            {
                tablesFolder.Children.Add(CreateTableTreeNode(tableMetadata, tablesFolder));
            }

            List<SavedQueryInfo> orderedQueries = savedQueryItems == null
                ? new List<SavedQueryInfo>()
                : savedQueryItems.OrderBy(item => item.Title).ToList();
            DatabaseTreeNode queriesFolder = CreateFolderTreeNode("查询", DatabaseObjectKind.QueriesFolder, schemaNode, orderedQueries.Count > 0 ? orderedQueries.Count + " 项" : string.Empty);
            foreach (SavedQueryInfo savedQuery in orderedQueries)
            {
                queriesFolder.Children.Add(CreateSavedQueryTreeNode(savedQuery, queriesFolder));
            }

            schemaNode.Children.Add(tablesFolder);
            schemaNode.Children.Add(queriesFolder);
        }

        /// <summary>
        /// 创建树上的分组文件夹节点。
        /// </summary>
        private static DatabaseTreeNode CreateFolderTreeNode(string name, DatabaseObjectKind kind, DatabaseTreeNode parent, string statusText)
        {
            return new DatabaseTreeNode
            {
                Name = name,
                Kind = kind,
                DisplayType = GetKindDisplayName(kind),
                Metadata = parent == null ? null : parent.Metadata,
                Parent = parent,
                StatusText = statusText ?? string.Empty,
                IconBrush = FolderBrush
            };
        }

        /// <summary>
        /// 创建表或视图节点，字段元数据只保留在 Metadata 中供补全和结构页使用。
        /// </summary>
        private static DatabaseTreeNode CreateTableTreeNode(DatabaseObjectNode metadataNode, DatabaseTreeNode parent)
        {
            return new DatabaseTreeNode
            {
                Name = metadataNode.Name,
                Kind = metadataNode.Kind,
                DisplayType = GetKindDisplayName(metadataNode.Kind),
                Metadata = metadataNode,
                Parent = parent,
                StatusText = string.Empty,
                IconBrush = GetNodeBrush(metadataNode.Kind)
            };
        }

        /// <summary>
        /// 创建已保存查询节点。
        /// </summary>
        private static DatabaseTreeNode CreateSavedQueryTreeNode(SavedQueryInfo savedQuery, DatabaseTreeNode parent)
        {
            string databaseName = savedQuery == null ? string.Empty : savedQuery.DatabaseName;
            string schemaName = savedQuery == null ? string.Empty : savedQuery.SchemaName;
            return new DatabaseTreeNode
            {
                Name = savedQuery == null ? string.Empty : savedQuery.Title,
                Kind = DatabaseObjectKind.SavedQuery,
                DisplayType = GetKindDisplayName(DatabaseObjectKind.SavedQuery),
                Parent = parent,
                Metadata = new DatabaseObjectNode
                {
                    Name = savedQuery == null ? string.Empty : savedQuery.Title,
                    Kind = DatabaseObjectKind.SavedQuery,
                    DatabaseName = databaseName,
                    SchemaName = schemaName
                },
                SavedQuery = savedQuery,
                StatusText = string.Empty,
                IconBrush = SavedQueryBrush
            };
        }

        /// <summary>
        /// 创建信息提示树节点。
        /// </summary>
        private static DatabaseTreeNode CreateInfoTreeNode(string message, DatabaseTreeNode parent, string displayType)
        {
            return new DatabaseTreeNode
            {
                Name = message,
                Kind = DatabaseObjectKind.Info,
                DisplayType = displayType,
                Parent = parent,
                IconBrush = InfoBrush
            };
        }

        /// <summary>
        /// 展平数据库元数据，便于按架构重新分组。
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
        /// 根据数据库类型补齐默认架构名称，SQLite 默认显示 main。
        /// </summary>
        private static string NormalizeSchemaName(ConnectionProfile profile, string schemaName)
        {
            if (!string.IsNullOrWhiteSpace(schemaName))
            {
                return schemaName;
            }

            if (profile != null && profile.ProviderType == DatabaseProviderType.SQLite)
            {
                return "main";
            }

            if (profile != null && profile.ProviderType == DatabaseProviderType.SqlServer)
            {
                return "dbo";
            }

            if (profile != null && !string.IsNullOrWhiteSpace(profile.DatabaseName))
            {
                return profile.DatabaseName;
            }

            return "main";
        }

        /// <summary>
        /// 规范化保存查询使用的数据库名称。
        /// </summary>
        private static string NormalizeSavedQueryDatabaseName(ConnectionProfile profile, string databaseName)
        {
            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                return databaseName.Trim();
            }

            if (profile != null && !string.IsNullOrWhiteSpace(profile.DatabaseName))
            {
                return profile.DatabaseName.Trim();
            }

            if (profile != null && profile.ProviderType == DatabaseProviderType.SQLite)
            {
                return "main";
            }

            if (profile != null && profile.ProviderType == DatabaseProviderType.Access && !string.IsNullOrWhiteSpace(profile.FilePath))
            {
                return Path.GetFileName(profile.FilePath);
            }

            return "default";
        }

        /// <summary>
        /// 规范化保存查询使用的架构名称。
        /// </summary>
        private static string NormalizeSavedQuerySchemaName(ConnectionProfile profile, string schemaName)
        {
            return NormalizeSchemaName(profile, schemaName);
        }

        /// <summary>
        /// 使用忽略大小写方式比较名称。
        /// </summary>
        private static bool SameName(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 根据对象类型返回树节点图标颜色。
        /// </summary>
        private static Brush GetNodeBrush(DatabaseObjectKind kind)
        {
            if (kind == DatabaseObjectKind.View)
            {
                return ViewBrush;
            }

            if (kind == DatabaseObjectKind.Column)
            {
                return ColumnBrush;
            }

            if (kind == DatabaseObjectKind.Info)
            {
                return InfoBrush;
            }

            return TableBrush;
        }

        /// <summary>
        /// 统一设置连接树节点状态，图标和文字颜色保持同步。
        /// </summary>
        private static void SetNodeVisualState(DatabaseTreeNode node, bool isConnected, string statusText, Brush iconBrush, Brush textBrush)
        {
            if (node == null)
            {
                return;
            }

            node.IsConnected = isConnected;
            node.StatusText = statusText ?? string.Empty;
            node.IconBrush = iconBrush;
            node.TextBrush = textBrush;
            node.UseCustomTextBrush = textBrush != null;
        }

        /// <summary>
        /// 创建 SQL 查询标签页。
        /// </summary>
        private QueryTabView CreateQueryTab(ConnectionProfile profile, string sql, bool executeAfterCreate, DatabaseObjectNode scopeNode)
        {
            return CreateQueryTab(profile, sql, executeAfterCreate, scopeNode, string.Empty);
        }

        /// <summary>
        /// 创建 SQL 查询标签页。
        /// </summary>
        private QueryTabView CreateQueryTab(ConnectionProfile profile, string sql, bool executeAfterCreate, DatabaseObjectNode scopeNode, string headerText)
        {
            return CreateQueryTab(profile, sql, executeAfterCreate, scopeNode, headerText, string.Empty);
        }

        /// <summary>
        /// 创建 SQL 查询标签页，可传入业务键避免同一个保存查询重复打开。
        /// </summary>
        private QueryTabView CreateQueryTab(ConnectionProfile profile, string sql, bool executeAfterCreate, DatabaseObjectNode scopeNode, string headerText, string tabKey)
        {
            if (profile == null)
            {
                TXMessageBox.Show(this, "请先选择或创建一个数据库连接。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            QueryTabView existingView = SelectTabByKey<QueryTabView>(tabKey);
            if (existingView != null)
            {
                return existingView;
            }

            QueryTabView view = new QueryTabView();
            view.Initialize(profile, GetMetadataForProfile(profile), scopeNode);
            view.ExecuteRequested += QueryView_ExecuteRequested;
            view.SaveRequested += QueryView_SaveRequested;
            if (!string.IsNullOrWhiteSpace(sql))
            {
                view.SetSql(sql);
            }

            TXTabItem tab = new TXTabItem();
            tab.Header = string.IsNullOrWhiteSpace(headerText) ? "查询 " + queryIndex++ : headerText;
            tab.IsClosable = true;
            tab.Content = view;
            tab.Tag = tabKey ?? string.Empty;
            tab.Close += Tab_Close;
            MainTabs.Items.Add(tab);
            MainTabs.SelectedItem = tab;

            if (executeAfterCreate)
            {
                view.RequestExecute();
            }

            return view;
        }

        /// <summary>
        /// 根据当前选中的表或视图生成默认查询语句。
        /// </summary>
        /// <param name="profile">当前连接配置。</param>
        /// <param name="scopeNode">当前树节点对应的元数据节点。</param>
        /// <returns>默认 SQL；没有合适目标时返回空字符串。</returns>
        private static string BuildDefaultQuerySql(ConnectionProfile profile, DatabaseObjectNode scopeNode)
        {
            if (profile == null || scopeNode == null)
            {
                return string.Empty;
            }

            if (scopeNode.Kind != DatabaseObjectKind.Table && scopeNode.Kind != DatabaseObjectKind.View)
            {
                return string.Empty;
            }

            DatabaseProviderBase provider = DatabaseProviderRegistry.GetProvider(profile.ProviderType) as DatabaseProviderBase;
            if (provider == null)
            {
                return string.Empty;
            }

            return provider.BuildDefaultSelectSql(scopeNode);
        }

        /// <summary>
        /// 处理查询页发出的 SQL 执行请求。
        /// </summary>
        private async void QueryView_ExecuteRequested(object sender, QueryExecuteRequestedEventArgs e)
        {
            QueryTabView view = sender as QueryTabView;
            if (view == null || e.Profile == null)
            {
                return;
            }

            try
            {
                view.SetMessage("正在执行 SQL...");
                IDatabaseProvider provider = DatabaseProviderRegistry.GetProvider(e.Profile.ProviderType);
                QueryExecutionResult result = await Task.Run(delegate
                {
                    e.CancellationToken.ThrowIfCancellationRequested();
                    return provider.Execute(e.Profile, e.Sql);
                }, e.CancellationToken);
                if (e.CancellationToken.IsCancellationRequested)
                {
                    view.SetMessage("已停止执行。");
                    return;
                }

                view.SetResult(result);
            }
            catch (OperationCanceledException)
            {
                view.SetMessage("已停止执行。");
            }
            catch (System.Exception ex)
            {
                if (e.CancellationToken.IsCancellationRequested)
                {
                    view.SetMessage("已停止执行。");
                    return;
                }

                view.SetMessage("执行失败：" + ex.Message);
                TXMessageBox.Show(this, ex.Message, "SQL 执行失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                view.EndExecution();
            }
        }

        /// <summary>
        /// 处理查询页发出的保存 SQL 请求。
        /// </summary>
        private void QueryView_SaveRequested(object sender, QuerySaveRequestedEventArgs e)
        {
            if (e == null || e.Profile == null)
            {
                return;
            }

            string databaseName = NormalizeSavedQueryDatabaseName(e.Profile, e.DatabaseName);
            string schemaName = NormalizeSavedQuerySchemaName(e.Profile, e.SchemaName);
            string defaultTitle = BuildNextSavedQueryTitle(e.Profile.Id, databaseName, schemaName);
            SaveQueryDialog dialog = new SaveQueryDialog(defaultTitle, "保存位置：" + e.Profile.Name + " / " + databaseName + " / " + schemaName);
            dialog.Owner = this;
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                SavedQueryInfo existingQuery = FindSavedQueryByTitle(e.Profile.Id, databaseName, schemaName, dialog.QueryTitle, null);
                if (existingQuery != null)
                {
                    MessageBoxResult overwriteResult = TXMessageBox.Show(this, "已经存在名称为“" + dialog.QueryTitle + "”的查询，是否覆盖原 SQL？", "保存查询", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (overwriteResult != MessageBoxResult.Yes)
                    {
                        StatusText.Text = "已取消保存查询。";
                        return;
                    }
                }

                SavedQueryInfo savedQuery = new SavedQueryInfo
                {
                    ConnectionId = e.Profile.Id,
                    ConnectionName = e.Profile.Name,
                    DatabaseName = databaseName,
                    SchemaName = schemaName,
                    Title = dialog.QueryTitle,
                    Sql = e.Sql
                };

                savedQueryStore.SaveOrUpdate(savedQueries, savedQuery);
                RefreshSavedQueryFoldersForConnection(e.Profile.Id);
                StatusText.Text = "查询已保存：" + dialog.QueryTitle;
            }
            catch (Exception ex)
            {
                TXMessageBox.Show(this, ex.Message, "保存查询失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 创建表分页预览标签页。
        /// </summary>
        private TablePreviewTabView CreateTablePreviewTab(ConnectionProfile profile, DatabaseObjectNode tableNode)
        {
            if (profile == null)
            {
                TXMessageBox.Show(this, "请先选择或创建一个数据库连接。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            if (tableNode == null)
            {
                TXMessageBox.Show(this, "请选择需要打开的表或视图。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            string tabKey = BuildTablePreviewTabKey(profile, tableNode);
            TablePreviewTabView existingView = SelectTabByKey<TablePreviewTabView>(tabKey);
            if (existingView != null)
            {
                return existingView;
            }

            TablePreviewTabView view = new TablePreviewTabView();
            view.PageRequested += TablePreviewView_PageRequested;
            view.CommitRequested += TablePreviewView_CommitRequested;

            TXTabItem tab = new TXTabItem();
            tab.Header = "数据 - " + tableNode.Name;
            tab.IsClosable = true;
            tab.Content = view;
            tab.Tag = tabKey;
            tab.Close += Tab_Close;
            MainTabs.Items.Add(tab);
            MainTabs.SelectedItem = tab;

            view.Initialize(profile, tableNode);
            view.RequestPage();
            return view;
        }

        /// <summary>
        /// 处理表预览页发出的分页加载请求。
        /// </summary>
        private async void TablePreviewView_PageRequested(object sender, TablePageRequestedEventArgs e)
        {
            TablePreviewTabView view = sender as TablePreviewTabView;
            if (view == null || e == null || e.Profile == null)
            {
                return;
            }

            try
            {
                IDatabaseProvider provider = DatabaseProviderRegistry.GetProvider(e.Profile.ProviderType);
                QueryExecutionResult result = await Task.Run(() => provider.PreviewTable(e.Profile, e.TableNode, e.PageIndex, e.PageSize, e.Filters));
                view.SetResult(result);
                StatusText.Text = "表数据加载完成。";
            }
            catch (System.Exception ex)
            {
                view.SetMessage("加载失败：" + ex.Message);
                TXMessageBox.Show(this, ex.Message, "表数据加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理表预览页提交行变更的请求。
        /// </summary>
        private async void TablePreviewView_CommitRequested(object sender, TableCommitRequestedEventArgs e)
        {
            TablePreviewTabView view = sender as TablePreviewTabView;
            if (view == null || e == null || e.Profile == null)
            {
                return;
            }

            try
            {
                IDatabaseProvider provider = DatabaseProviderRegistry.GetProvider(e.Profile.ProviderType);
                int affectedRows = await Task.Run(() => provider.ApplyTableChanges(e.Profile, e.TableNode, e.Changes));
                view.SetMessage("提交成功，影响 " + affectedRows + " 行，正在刷新...");
                view.RequestPage();
                StatusText.Text = "表数据修改已提交。";
            }
            catch (Exception exception)
            {
                view.SetCommitFailure("提交失败：" + exception.Message);
                TXMessageBox.Show(this, "提交表数据修改失败：" + exception.Message, "TXDB", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 打开表结构标签页。
        /// </summary>
        private async Task OpenStructureTabAsync(DatabaseTreeNode node)
        {
            if (node == null || (node.Kind != DatabaseObjectKind.Table && node.Kind != DatabaseObjectKind.View))
            {
                return;
            }

            ConnectionProfile profile = GetProfileForNode(node);
            if (profile == null)
            {
                return;
            }

            string tabKey = BuildStructureTabKey(profile, node.Metadata);
            if (SelectTabByKey<FrameworkElement>(tabKey) != null)
            {
                return;
            }

            await CreateStructureViewAsync(profile, node.Metadata, false);
        }

        private async Task CreateStructureViewAsync(ConnectionProfile profile, DatabaseObjectNode node, bool isNew)
        {
            TableStructureTabView view = new TableStructureTabView();
            TXTabItem tab = new TXTabItem();
            tab.Header = isNew ? "新建表" : "结构 - " + node.Name;
            tab.IsClosable = true;
            tab.Content = view;
            tab.Tag = isNew ? "new-table:" + Guid.NewGuid().ToString("N") : BuildStructureTabKey(profile, node);
            tab.Close += Tab_Close;
            view.SchemaSaved += StructureView_SchemaSaved;
            view.SchemaSaving += StructureView_SchemaSaving;
            view.ConfirmBeforeSave = () => CanChangeTableStructure(profile, view.TableNode);
            MainTabs.Items.Add(tab);
            MainTabs.SelectedItem = tab;
            await view.InitializeAsync(profile, node, isNew);
            StatusText.Text = isNew ? "填写表名和字段后提交创建表。" : "已打开表结构：" + node.Name;
        }

        /// <summary>
        /// 关闭可关闭标签页。
        /// </summary>
        private void Tab_Close(object sender, RoutedEventArgs e)
        {
            TXTabItem tab = sender as TXTabItem;
            if (tab != null)
            {
                TableStructureTabView structure = tab.Content as TableStructureTabView;
                if (structure != null && !structure.ConfirmClose()) { e.Handled = true; return; }
                CleanupTab(tab);
                MainTabs.Items.Remove(tab);
            }
        }

        /// <summary>
        /// 按业务键查找标签页，找到后直接切换过去。
        /// </summary>
        private T SelectTabByKey<T>(string tabKey) where T : class
        {
            TXTabItem tab = FindTabByKey(tabKey);
            if (tab == null)
            {
                return null;
            }

            MainTabs.SelectedItem = tab;
            return tab.Content as T;
        }

        /// <summary>
        /// 按业务键查找已经打开的标签页。
        /// </summary>
        private TXTabItem FindTabByKey(string tabKey)
        {
            if (MainTabs == null || string.IsNullOrWhiteSpace(tabKey))
            {
                return null;
            }

            foreach (object item in MainTabs.Items)
            {
                TXTabItem tab = item as TXTabItem;
                if (tab != null && SameName(Convert.ToString(tab.Tag), tabKey))
                {
                    return tab;
                }
            }

            return null;
        }

        /// <summary>
        /// 按业务键更新标签标题，保存查询重命名后使用。
        /// </summary>
        private void UpdateTabHeaderByKey(string tabKey, string headerText)
        {
            TXTabItem tab = FindTabByKey(tabKey);
            if (tab != null)
            {
                tab.Header = headerText ?? string.Empty;
            }
        }

        /// <summary>
        /// 生成保存查询标签页的唯一键。
        /// </summary>
        private static string BuildSavedQueryTabKey(SavedQueryInfo query)
        {
            return query == null ? string.Empty : "saved-query:" + (query.Id ?? string.Empty);
        }

        /// <summary>
        /// 生成表预览标签页的唯一键。
        /// </summary>
        private static string BuildTablePreviewTabKey(ConnectionProfile profile, DatabaseObjectNode tableNode)
        {
            return BuildObjectTabKey("table-preview", profile, tableNode);
        }

        /// <summary>
        /// 生成表结构标签页的唯一键。
        /// </summary>
        private static string BuildStructureTabKey(ConnectionProfile profile, DatabaseObjectNode tableNode)
        {
            return BuildObjectTabKey("table-structure", profile, tableNode);
        }

        /// <summary>
        /// 生成数据库对象标签页的稳定键，避免同一对象重复打开。
        /// </summary>
        private static string BuildObjectTabKey(string prefix, ConnectionProfile profile, DatabaseObjectNode node)
        {
            if (profile == null || node == null)
            {
                return string.Empty;
            }

            return string.Join("|", new[]
            {
                prefix ?? string.Empty,
                profile.Id ?? string.Empty,
                node.DatabaseName ?? string.Empty,
                node.SchemaName ?? string.Empty,
                node.Kind.ToString(),
                node.Name ?? string.Empty
            });
        }

        /// <summary>
        /// 清理所有标签页持有的事件和临时资源。
        /// </summary>
        private void CleanupAllTabs()
        {
            if (MainTabs == null)
            {
                return;
            }

            foreach (object item in MainTabs.Items)
            {
                CleanupTab(item as TXTabItem);
            }
        }

        /// <summary>
        /// 清理单个标签页中的查询页或表预览页事件。
        /// </summary>
        private void CleanupTab(TXTabItem tab)
        {
            if (tab == null)
            {
                return;
            }

            TableStructureTabView structureView = tab.Content as TableStructureTabView;
            if (structureView != null)
            {
                structureView.SchemaSaved -= StructureView_SchemaSaved;
                structureView.SchemaSaving -= StructureView_SchemaSaving;
                structureView.Cleanup();
                return;
            }

            QueryTabView queryView = tab.Content as QueryTabView;
            if (queryView != null)
            {
                queryView.ExecuteRequested -= QueryView_ExecuteRequested;
                queryView.SaveRequested -= QueryView_SaveRequested;
                queryView.Cleanup();
                return;
            }

            TablePreviewTabView previewView = tab.Content as TablePreviewTabView;
            if (previewView != null)
            {
                previewView.PageRequested -= TablePreviewView_PageRequested;
                previewView.CommitRequested -= TablePreviewView_CommitRequested;
            }
        }

        /// 获取双击命中的树节点，避免使用旧选中项导致误操作。
        /// </summary>
        private DatabaseTreeNode GetDoubleClickTreeNode(MouseButtonEventArgs e)
        {
            DependencyObject source = e == null ? null : e.OriginalSource as DependencyObject;
            TreeViewItem item = FindParent<TreeViewItem>(source);
            if (item != null)
            {
                return item.DataContext as DatabaseTreeNode;
            }

            return GetSelectedTreeNode();
        }

        /// <summary>
        /// 获取左侧连接树当前选中的业务节点。
        /// </summary>
        private DatabaseTreeNode GetSelectedTreeNode()
        {
            return ConnectionTree.SelectedItem as DatabaseTreeNode;
        }

        /// <summary>
        /// 获取新建查询页应默认定位到的数据库或架构节点。
        /// </summary>
        private DatabaseObjectNode GetSelectedScopeNode()
        {
            DatabaseTreeNode node = GetSelectedTreeNode();
            if (node == null)
            {
                return null;
            }

            if (node.Kind == DatabaseObjectKind.Database ||
                node.Kind == DatabaseObjectKind.Schema ||
                node.Kind == DatabaseObjectKind.Table ||
                node.Kind == DatabaseObjectKind.View ||
                node.Kind == DatabaseObjectKind.QueriesFolder ||
                node.Kind == DatabaseObjectKind.SavedQuery)
            {
                return node.Metadata;
            }

            return null;
        }

        /// <summary>
        /// 打开一个保存查询节点。
        /// </summary>
        private void OpenSavedQueryNode(DatabaseTreeNode node)
        {
            if (node == null || node.Kind != DatabaseObjectKind.SavedQuery || node.SavedQuery == null)
            {
                return;
            }

            ConnectionProfile profile = GetProfileForNode(node) ?? profiles.FirstOrDefault(item => SameName(item.Id, node.SavedQuery.ConnectionId));
            if (profile == null)
            {
                TXMessageBox.Show(this, "保存查询所属连接不存在。", "打开查询", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CreateQueryTab(profile, node.SavedQuery.Sql, false, node.Metadata, node.SavedQuery.Title, BuildSavedQueryTabKey(node.SavedQuery));
        }

        /// <summary>
        /// 获取当前节点对应的保存查询集合，查询文件夹返回整组，查询节点返回单条。
        /// </summary>
        private IList<SavedQueryInfo> GetSavedQueriesForNode(DatabaseTreeNode node)
        {
            if (node == null)
            {
                return new List<SavedQueryInfo>();
            }

            if (node.Kind == DatabaseObjectKind.SavedQuery && node.SavedQuery != null)
            {
                return new List<SavedQueryInfo> { node.SavedQuery };
            }

            if (node.Kind != DatabaseObjectKind.QueriesFolder)
            {
                return new List<SavedQueryInfo>();
            }

            ConnectionProfile profile = GetProfileForNode(node);
            if (profile == null)
            {
                return new List<SavedQueryInfo>();
            }

            string databaseName = GetNodeDatabaseName(node, profile);
            string schemaName = GetNodeSchemaName(node, profile);
            return savedQueries
                .Where(item => SameName(item.ConnectionId, profile.Id) &&
                               SameName(NormalizeSavedQueryDatabaseName(profile, item.DatabaseName), databaseName) &&
                               SameName(NormalizeSavedQuerySchemaName(profile, item.SchemaName), schemaName))
                .OrderBy(item => item.Title)
                .ToList();
        }

        /// <summary>
        /// 刷新指定连接下所有已加载的查询文件夹。
        /// </summary>
        private void RefreshSavedQueryFoldersForConnection(string connectionId)
        {
            foreach (DatabaseTreeNode rootNode in ConnectionNodes)
            {
                if (rootNode.Kind != DatabaseObjectKind.Connection ||
                    rootNode.Profile == null ||
                    !SameName(rootNode.Profile.Id, connectionId))
                {
                    continue;
                }

                foreach (DatabaseTreeNode node in EnumerateTreeNodes(rootNode))
                {
                    if (node.Kind == DatabaseObjectKind.QueriesFolder)
                    {
                        FillSavedQueryFolder(node);
                    }
                }
            }
        }

        /// <summary>
        /// 按查询文件夹所属连接、数据库和架构重新填充保存查询节点。
        /// </summary>
        private void FillSavedQueryFolder(DatabaseTreeNode queriesFolder)
        {
            if (queriesFolder == null || queriesFolder.Kind != DatabaseObjectKind.QueriesFolder)
            {
                return;
            }

            IList<SavedQueryInfo> queries = GetSavedQueriesForNode(queriesFolder);
            queriesFolder.Children.Clear();
            foreach (SavedQueryInfo query in queries)
            {
                queriesFolder.Children.Add(CreateSavedQueryTreeNode(query, queriesFolder));
            }

            queriesFolder.StatusText = queries.Count > 0 ? queries.Count + " 项" : string.Empty;
        }

        /// <summary>
        /// 深度遍历树节点集合。
        /// </summary>
        private static IEnumerable<DatabaseTreeNode> EnumerateTreeNodes(DatabaseTreeNode node)
        {
            if (node == null)
            {
                yield break;
            }

            yield return node;
            foreach (DatabaseTreeNode child in node.Children)
            {
                foreach (DatabaseTreeNode descendant in EnumerateTreeNodes(child))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// 根据树节点推导保存查询所属数据库名称。
        /// </summary>
        private static string GetNodeDatabaseName(DatabaseTreeNode node, ConnectionProfile profile)
        {
            if (node != null && node.SavedQuery != null)
            {
                return NormalizeSavedQueryDatabaseName(profile, node.SavedQuery.DatabaseName);
            }

            string databaseName = string.Empty;
            if (node != null && node.Metadata != null)
            {
                databaseName = string.IsNullOrWhiteSpace(node.Metadata.DatabaseName) ? node.Metadata.Name : node.Metadata.DatabaseName;
            }

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                DatabaseTreeNode databaseNode = GetDatabaseNode(node);
                if (databaseNode != null)
                {
                    databaseName = databaseNode.Metadata == null || string.IsNullOrWhiteSpace(databaseNode.Metadata.DatabaseName)
                        ? databaseNode.Name
                        : databaseNode.Metadata.DatabaseName;
                }
            }

            return NormalizeSavedQueryDatabaseName(profile, databaseName);
        }

        /// <summary>
        /// 根据树节点推导保存查询所属架构名称。
        /// </summary>
        private static string GetNodeSchemaName(DatabaseTreeNode node, ConnectionProfile profile)
        {
            if (node != null && node.SavedQuery != null)
            {
                return NormalizeSavedQuerySchemaName(profile, node.SavedQuery.SchemaName);
            }

            string schemaName = string.Empty;
            if (node != null && node.Metadata != null)
            {
                schemaName = node.Metadata.SchemaName;
            }

            if (string.IsNullOrWhiteSpace(schemaName))
            {
                DatabaseTreeNode schemaNode = GetSchemaNode(node);
                if (schemaNode != null)
                {
                    schemaName = schemaNode.Name;
                }
            }

            return NormalizeSavedQuerySchemaName(profile, schemaName);
        }

        /// <summary>
        /// 从任意树节点向上查找架构节点。
        /// </summary>
        private static DatabaseTreeNode GetSchemaNode(DatabaseTreeNode node)
        {
            DatabaseTreeNode current = node;
            while (current != null)
            {
                if (current.Kind == DatabaseObjectKind.Schema)
                {
                    return current;
                }

                current = current.Parent;
            }

            return null;
        }

        /// <summary>
        /// 生成同一连接、数据库和架构下的下一个默认查询名称。
        /// </summary>
        private string BuildNextSavedQueryTitle(string connectionId, string databaseName, string schemaName)
        {
            int index = 1;
            while (SavedQueryTitleExists(connectionId, databaseName, schemaName, "查询" + index, null))
            {
                index++;
            }

            return "查询" + index;
        }

        /// <summary>
        /// 根据候选名称生成不会和现有查询冲突的名称，主要用于批量导入。
        /// </summary>
        private string BuildAvailableSavedQueryTitle(string connectionId, string databaseName, string schemaName, string preferredTitle, string ignoreQueryId)
        {
            string baseTitle = string.IsNullOrWhiteSpace(preferredTitle)
                ? BuildNextSavedQueryTitle(connectionId, databaseName, schemaName)
                : preferredTitle.Trim();
            if (!SavedQueryTitleExists(connectionId, databaseName, schemaName, baseTitle, ignoreQueryId))
            {
                return baseTitle;
            }

            int index = 1;
            string candidate;
            do
            {
                candidate = baseTitle + "（" + index + "）";
                index++;
            }
            while (SavedQueryTitleExists(connectionId, databaseName, schemaName, candidate, ignoreQueryId));

            return candidate;
        }

        /// <summary>
        /// 判断同一连接、数据库和架构下是否存在指定标题的查询。
        /// </summary>
        private bool SavedQueryTitleExists(string connectionId, string databaseName, string schemaName, string title, string ignoreQueryId)
        {
            return FindSavedQueryByTitle(connectionId, databaseName, schemaName, title, ignoreQueryId) != null;
        }

        /// <summary>
        /// 按保存范围和标题查找已保存查询，可排除当前正在重命名的查询。
        /// </summary>
        private SavedQueryInfo FindSavedQueryByTitle(string connectionId, string databaseName, string schemaName, string title, string ignoreQueryId)
        {
            return savedQueries.FirstOrDefault(item =>
                (string.IsNullOrWhiteSpace(ignoreQueryId) || !SameName(item.Id, ignoreQueryId)) &&
                SameName(item.ConnectionId, connectionId) &&
                SameName(item.DatabaseName, databaseName) &&
                SameName(item.SchemaName, schemaName) &&
                SameName(item.Title, title));
        }

        /// <summary>
        /// 根据当前节点生成导出文件名。
        /// </summary>
        private static string BuildSavedQueryExportFileName(DatabaseTreeNode node)
        {
            if (node != null && node.Kind == DatabaseObjectKind.SavedQuery && node.SavedQuery != null)
            {
                return AppStoragePaths.ToSafeFileName(node.SavedQuery.Title) + ".json";
            }

            ConnectionProfile profile = GetProfileForNode(node);
            string connectionName = profile == null ? "queries" : profile.Name;
            string databaseName = GetNodeDatabaseName(node, profile);
            string schemaName = GetNodeSchemaName(node, profile);
            return AppStoragePaths.ToSafeFileName(connectionName + "_" + databaseName + "_" + schemaName + "_queries") + ".json";
        }

        /// <summary>
        /// 获取指定连接已经加载的元数据缓存。
        /// </summary>
        private IList<DatabaseObjectNode> GetMetadataForProfile(ConnectionProfile profile)
        {
            if (profile == null)
            {
                return new List<DatabaseObjectNode>();
            }

            IList<DatabaseObjectNode> metadata;
            if (metadataCache.TryGetValue(profile.Id, out metadata))
            {
                return metadata;
            }

            return new List<DatabaseObjectNode>();
        }

        /// <summary>
        /// 替换当前连接缓存中的单个数据库元数据，并刷新全局补全词库。
        /// </summary>
        private void ReplaceDatabaseMetadata(ConnectionProfile profile, DatabaseObjectNode databaseMetadata)
        {
            if (profile == null || databaseMetadata == null)
            {
                return;
            }

            IList<DatabaseObjectNode> cachedMetadata;
            List<DatabaseObjectNode> roots = metadataCache.TryGetValue(profile.Id, out cachedMetadata)
                ? cachedMetadata.ToList()
                : new List<DatabaseObjectNode>();
            string databaseName = string.IsNullOrWhiteSpace(databaseMetadata.Name) ? databaseMetadata.DatabaseName : databaseMetadata.Name;
            int index = roots.FindIndex(item => item.Kind == DatabaseObjectKind.Database &&
                                               string.Equals(string.IsNullOrWhiteSpace(item.Name) ? item.DatabaseName : item.Name, databaseName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                roots[index] = databaseMetadata;
            }
            else
            {
                roots.Add(databaseMetadata);
            }

            metadataCache[profile.Id] = roots;
            completionService.SetMetadata(roots);
        }

        /// <summary>
        /// 获取当前选中节点所属的连接配置。
        /// </summary>
        private ConnectionProfile GetSelectedProfile()
        {
            return GetProfileForNode(GetSelectedTreeNode());
        }

        /// <summary>
        /// 从任意树节点向上查找连接配置。
        /// </summary>
        private static ConnectionProfile GetProfileForNode(DatabaseTreeNode node)
        {
            DatabaseTreeNode connectionNode = GetConnectionNode(node);
            return connectionNode == null ? null : connectionNode.Profile;
        }

        /// <summary>
        /// 从任意树节点向上查找连接节点。
        /// </summary>
        private static DatabaseTreeNode GetConnectionNode(DatabaseTreeNode node)
        {
            DatabaseTreeNode current = node;
            while (current != null)
            {
                if (current.Kind == DatabaseObjectKind.Connection)
                {
                    return current;
                }

                current = current.Parent;
            }

            return null;
        }

        /// <summary>
        /// 从任意树节点向上查找数据库节点。
        /// </summary>
        private static DatabaseTreeNode GetDatabaseNode(DatabaseTreeNode node)
        {
            DatabaseTreeNode current = node;
            while (current != null)
            {
                if (current.Kind == DatabaseObjectKind.Database)
                {
                    return current;
                }

                current = current.Parent;
            }

            return null;
        }

        /// <summary>
        /// 判断数据库节点是否已经读取过对象。
        /// </summary>
        private static bool IsDatabaseNodeLoaded(DatabaseTreeNode node)
        {
            if (node == null || node.Kind != DatabaseObjectKind.Database)
            {
                return false;
            }

            return node.Children.Count > 0 || (node.Metadata != null && node.Metadata.Children.Count > 0);
        }

        /// <summary>
        /// 设置右键菜单项显隐。
        /// </summary>
        private static void SetMenuItemVisible(UIElement element, bool visible)
        {
            if (element != null)
            {
                element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 获取当前节点用于显示的短名称。
        /// </summary>
        private static string GetNodeDisplayName(DatabaseTreeNode node)
        {
            return node == null ? string.Empty : node.Name;
        }

        /// <summary>
        /// 获取当前节点的完整名称，便于数据库和表右键复制。
        /// </summary>
        private static string GetNodeFullName(DatabaseTreeNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            if (node.Kind == DatabaseObjectKind.Connection)
            {
                return node.Name;
            }

            return BuildNodePath(node);
        }

        /// <summary>
        /// 从当前节点向上拼接完整路径。
        /// </summary>
        private static string BuildNodePath(DatabaseTreeNode node)
        {
            List<string> parts = new List<string>();
            DatabaseTreeNode current = node;
            while (current != null && current.Kind != DatabaseObjectKind.Connection)
            {
                if (!IsTreeFolderNode(current.Kind))
                {
                    parts.Insert(0, current.Name);
                }

                current = current.Parent;
            }

            return string.Join(".", parts.ToArray());
        }

        /// <summary>
        /// 判断节点是否只是界面分组，不参与复制完整对象名称。
        /// </summary>
        private static bool IsTreeFolderNode(DatabaseObjectKind kind)
        {
            return kind == DatabaseObjectKind.Folder ||
                   kind == DatabaseObjectKind.TablesFolder ||
                   kind == DatabaseObjectKind.ViewsFolder ||
                   kind == DatabaseObjectKind.QueriesFolder;
        }

        /// <summary>
        /// 获取对象类型显示文本。
        /// </summary>
        private static string GetKindDisplayName(DatabaseObjectKind kind)
        {
            if (kind == DatabaseObjectKind.Connection)
            {
                return "连接";
            }

            if (kind == DatabaseObjectKind.Database)
            {
                return "数据库";
            }

            if (kind == DatabaseObjectKind.Schema)
            {
                return "架构";
            }

            if (kind == DatabaseObjectKind.Table)
            {
                return "表";
            }

            if (kind == DatabaseObjectKind.View)
            {
                return "视图";
            }

            if (kind == DatabaseObjectKind.Column)
            {
                return "字段";
            }

            if (kind == DatabaseObjectKind.Index)
            {
                return "索引";
            }

            if (kind == DatabaseObjectKind.TablesFolder)
            {
                return "表";
            }

            if (kind == DatabaseObjectKind.QueriesFolder)
            {
                return "查询";
            }

            if (kind == DatabaseObjectKind.SavedQuery)
            {
                return "已保存查询";
            }

            return "信息";
        }

        /// <summary>
        /// 沿可视树向上查找指定父级。
        /// </summary>
        private static T FindParent<T>(DependencyObject source) where T : DependencyObject
        {
            DependencyObject current = source;
            while (current != null)
            {
                T match = current as T;
                if (match != null)
                {
                    return match;
                }

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
