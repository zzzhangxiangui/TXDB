using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TX.WpfUI.Controls;
using TXDB.Data;
using TXDB.Models;

namespace TXDB.Views
{
    /// <summary>
    /// 数据库连接新增和编辑弹窗。
    /// </summary>
    public partial class ConnectionDialog : Window
    {
        private readonly bool isEditMode;

        /// <summary>
        /// 初始化新增连接弹窗。
        /// </summary>
        public ConnectionDialog()
            : this(null)
        {
        }

        /// <summary>
        /// 初始化新增或编辑连接弹窗。
        /// </summary>
        public ConnectionDialog(ConnectionProfile profile)
        {
            InitializeComponent();
            ProviderBox.ItemsSource = BuildProviderOptions();

            if (profile == null)
            {
                Profile = CreateDefaultProfile();
                isEditMode = false;
            }
            else
            {
                Profile = profile.Clone();
                isEditMode = true;
            }

            LoadProfileToControls();
            Title = isEditMode ? "编辑数据库连接" : "新建数据库连接";
        }

        /// <summary>
        /// 弹窗确认后的连接配置。
        /// </summary>
        public ConnectionProfile Profile { get; private set; }

        /// <summary>
        /// 构建数据库类型下拉项。
        /// </summary>
        private static IList<ProviderOption> BuildProviderOptions()
        {
            List<ProviderOption> options = new List<ProviderOption>();
            foreach (IDatabaseProvider provider in DatabaseProviderRegistry.GetProviders())
            {
                options.Add(new ProviderOption
                {
                    ProviderType = provider.ProviderType,
                    DisplayName = provider.DisplayName
                });
            }

            return options;
        }

        /// <summary>
        /// 创建新增连接时使用的默认配置。
        /// </summary>
        private static ConnectionProfile CreateDefaultProfile()
        {
            return new ConnectionProfile
            {
                ProviderType = DatabaseProviderType.SqlServer,
                Name = "新建连接",
                Host = ".",
                DatabaseName = "master",
                UseWindowsAuthentication = true
            };
        }

        /// <summary>
        /// 将连接配置加载到界面控件。
        /// </summary>
        private void LoadProfileToControls()
        {
            NameBox.Text = Profile.Name;
            ProviderBox.SelectedValue = Profile.ProviderType;
            HostBox.Text = Profile.Host;
            PortBox.Text = Profile.Port;
            DatabaseBox.Text = Profile.DatabaseName;
            UserNameBox.Text = Profile.UserName;
            PasswordBox.Password = Profile.Password;
            FilePicker.Path = Profile.FilePath;
            RawConnectionBox.Text = Profile.RawConnectionString;
            WindowsAuthBox.IsChecked = Profile.UseWindowsAuthentication;
            SavePasswordBox.IsChecked = Profile.SavePassword;
            UpdateFieldVisibility();
        }

        /// <summary>
        /// 将界面输入写回连接配置。
        /// </summary>
        private void SaveControlsToProfile()
        {
            DatabaseProviderType providerType = GetSelectedProviderType();
            Profile.Name = NameBox.Text.Trim();
            Profile.ProviderType = providerType;
            Profile.Host = HostBox.Text.Trim();
            Profile.Port = PortBox.Text.Trim();
            Profile.DatabaseName = DatabaseBox.Text.Trim();
            Profile.UserName = UserNameBox.Text.Trim();
            Profile.Password = PasswordBox.Password;
            Profile.FilePath = FilePicker.Path == null ? string.Empty : FilePicker.Path.Trim();
            Profile.RawConnectionString = RawConnectionBox.Text.Trim();
            Profile.UseWindowsAuthentication = providerType == DatabaseProviderType.SqlServer && WindowsAuthBox.IsChecked == true;
            Profile.SavePassword = SavePasswordBox.IsChecked == true;
        }

        /// <summary>
        /// 获取当前选择的数据库类型。
        /// </summary>
        private DatabaseProviderType GetSelectedProviderType()
        {
            if (ProviderBox.SelectedValue is DatabaseProviderType)
            {
                return (DatabaseProviderType)ProviderBox.SelectedValue;
            }

            return DatabaseProviderType.SqlServer;
        }

        /// <summary>
        /// 根据数据库类型调整输入区域。
        /// </summary>
        private void UpdateFieldVisibility()
        {
            DatabaseProviderType providerType = GetSelectedProviderType();
            bool isFileDatabase = providerType == DatabaseProviderType.SQLite || providerType == DatabaseProviderType.Access;
            bool isSqlServer = providerType == DatabaseProviderType.SqlServer;

            FilePanel.Visibility = isFileDatabase ? Visibility.Visible : Visibility.Collapsed;
            ServerPanel.Visibility = isFileDatabase ? Visibility.Collapsed : Visibility.Visible;
            WindowsAuthBox.Visibility = isSqlServer ? Visibility.Visible : Visibility.Collapsed;
            CredentialPanel.Visibility = isSqlServer && WindowsAuthBox.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;

            if (providerType == DatabaseProviderType.MySQL && string.IsNullOrWhiteSpace(PortBox.Text))
            {
                PortBox.Text = "3306";
            }
            else if (providerType == DatabaseProviderType.Oracle && string.IsNullOrWhiteSpace(PortBox.Text))
            {
                PortBox.Text = "1521";
            }

            if (providerType == DatabaseProviderType.MySQL && SameName(DatabaseBox.Text, "master"))
            {
                // master 是 SQL Server 的默认库名，切到 MySQL 时继续带着它会导致账号被判定没有 master 库权限。
                DatabaseBox.Text = string.Empty;
            }
        }

        /// <summary>
        /// 数据库类型变化时刷新字段显示。
        /// </summary>
        private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFieldVisibility();
        }

        /// <summary>
        /// Windows 身份验证变化时刷新账号密码区域。
        /// </summary>
        private void WindowsAuthBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateFieldVisibility();
        }

        /// <summary>
        /// 处理测试连接按钮。
        /// </summary>
        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveControlsToProfile();
                ValidateProfile();
                DatabaseProviderRegistry.GetProvider(Profile.ProviderType).TestConnection(Profile);
                TXMessageBox.Show(this, "连接成功。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TXMessageBox.Show(this, ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理确定按钮。
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveControlsToProfile();
                ValidateProfile();
                DialogResult = true;
            }
            catch (Exception ex)
            {
                TXMessageBox.Show(this, ex.Message, "连接配置不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 处理取消按钮。
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>
        /// 校验必要连接参数。
        /// </summary>
        private void ValidateProfile()
        {
            if (string.IsNullOrWhiteSpace(Profile.Name))
            {
                throw new InvalidOperationException("请输入连接名称。");
            }

            if (!string.IsNullOrWhiteSpace(Profile.RawConnectionString))
            {
                return;
            }

            if (Profile.ProviderType == DatabaseProviderType.SQLite || Profile.ProviderType == DatabaseProviderType.Access)
            {
                if (string.IsNullOrWhiteSpace(Profile.FilePath))
                {
                    throw new InvalidOperationException("请选择数据库文件。");
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(Profile.Host))
            {
                throw new InvalidOperationException("请输入服务器地址。");
            }

            bool needUserName = Profile.ProviderType != DatabaseProviderType.SqlServer || !Profile.UseWindowsAuthentication;
            if (needUserName && string.IsNullOrWhiteSpace(Profile.UserName))
            {
                throw new InvalidOperationException("请输入用户名。");
            }
        }

        /// <summary>
        /// 数据库类型下拉框显示对象。
        /// </summary>
        private class ProviderOption
        {
            /// <summary>
            /// 数据库类型。
            /// </summary>
            public DatabaseProviderType ProviderType { get; set; }

            /// <summary>
            /// 数据库类型显示名称。
            /// </summary>
            public string DisplayName { get; set; }

            /// <summary>
            /// 下拉框模板读取对象文本时返回数据库类型名称。
            /// </summary>
            public override string ToString()
            {
                return DisplayName ?? string.Empty;
            }
        }

        /// <summary>
        /// 使用忽略大小写方式比较界面文本。
        /// </summary>
        private static bool SameName(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
