using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using TX.WpfUI.Controls;
using TXDB.Data;
using TXDB.Models;
using TXDB.Services;

namespace TXDB.Views
{
    /// <summary>
    /// 连接导出选择弹窗，允许用户选择本次需要写入 JSON 的连接。
    /// </summary>
    public partial class ConnectionExportDialog : Window
    {
        private readonly ObservableCollection<ConnectionExportItem> exportItems;

        /// <summary>
        /// 初始化连接导出选择弹窗。
        /// </summary>
        public ConnectionExportDialog(IEnumerable<ConnectionProfile> profiles)
        {
            InitializeComponent();

            exportItems = new ObservableCollection<ConnectionExportItem>();
            foreach (ConnectionProfile profile in (profiles ?? new List<ConnectionProfile>()).Where(item => item != null && !item.IsDesignSample).OrderBy(item => item.Name))
            {
                exportItems.Add(new ConnectionExportItem(profile));
            }

            ConnectionList.ItemsSource = exportItems;
            SelectedProfiles = new List<ConnectionProfile>();
            UpdateSelectedCountText();
        }

        /// <summary>
        /// 用户最终选择导出的连接配置。
        /// </summary>
        public IList<ConnectionProfile> SelectedProfiles { get; private set; }

        /// <summary>
        /// 勾选状态变化时刷新底部数量提示。
        /// </summary>
        private void SelectionBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSelectedCountText();
        }

        /// <summary>
        /// 处理全选按钮。
        /// </summary>
        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (ConnectionExportItem item in exportItems)
            {
                item.IsSelected = true;
            }

            ConnectionList.Items.Refresh();
            UpdateSelectedCountText();
        }

        /// <summary>
        /// 处理全不选按钮。
        /// </summary>
        private void SelectNoneButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (ConnectionExportItem item in exportItems)
            {
                item.IsSelected = false;
            }

            ConnectionList.Items.Refresh();
            UpdateSelectedCountText();
        }

        /// <summary>
        /// 处理取消按钮。
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>
        /// 处理导出按钮，空选择时给出提示。
        /// </summary>
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedProfiles = exportItems
                .Where(item => item.IsSelected)
                .Select(item => item.Profile.Clone())
                .ToList();

            if (SelectedProfiles.Count == 0)
            {
                TXMessageBox.Show(this, "请至少选择一个需要导出的连接。", "导出连接", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        }

        /// <summary>
        /// 刷新已选择连接数量。
        /// </summary>
        private void UpdateSelectedCountText()
        {
            int selectedCount = exportItems.Count(item => item.IsSelected);
            SelectedCountText.Text = "已选择 " + selectedCount + " 个连接，共 " + exportItems.Count + " 个";
        }

        /// <summary>
        /// 导出列表中的单行显示对象。
        /// </summary>
        private class ConnectionExportItem : NotifyObject
        {
            private bool isSelected;

            /// <summary>
            /// 根据连接配置创建导出列表行。
            /// </summary>
            public ConnectionExportItem(ConnectionProfile profile)
            {
                Profile = profile;
                IsSelected = true;
                Name = string.IsNullOrWhiteSpace(profile.Name) ? "新建连接" : profile.Name;
                Description = DatabaseProviderRegistry.GetDisplayName(profile.ProviderType) + "  " + BuildTargetText(profile);
            }

            /// <summary>
            /// 原始连接配置。
            /// </summary>
            public ConnectionProfile Profile { get; private set; }

            /// <summary>
            /// 是否选择导出当前连接。
            /// </summary>
            public bool IsSelected
            {
                get { return isSelected; }
                set
                {
                    isSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }

            /// <summary>
            /// 连接名称。
            /// </summary>
            public string Name { get; private set; }

            /// <summary>
            /// 连接类型和目标地址说明。
            /// </summary>
            public string Description { get; private set; }

            /// <summary>
            /// 生成连接目标描述，便于用户识别同名连接。
            /// </summary>
            private static string BuildTargetText(ConnectionProfile profile)
            {
                if (profile.ProviderType == DatabaseProviderType.SQLite || profile.ProviderType == DatabaseProviderType.Access)
                {
                    return string.IsNullOrWhiteSpace(profile.FilePath) ? "未配置文件路径" : profile.FilePath;
                }

                string host = string.IsNullOrWhiteSpace(profile.Host) ? "未配置服务器" : profile.Host;
                string port = string.IsNullOrWhiteSpace(profile.Port) ? string.Empty : ":" + profile.Port;
                string databaseName = string.IsNullOrWhiteSpace(profile.DatabaseName) ? string.Empty : " / " + profile.DatabaseName;
                return host + port + databaseName;
            }
        }
    }
}
