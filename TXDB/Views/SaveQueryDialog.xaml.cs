using System;
using System.Windows;
using System.Windows.Input;
using TX.WpfUI.Controls;

namespace TXDB.Views
{
    /// <summary>
    /// 保存查询语句时输入标题的弹窗。
    /// </summary>
    public partial class SaveQueryDialog : Window
    {
        /// <summary>
        /// 初始化保存查询弹窗。
        /// </summary>
        public SaveQueryDialog(string defaultTitle, string scopeText)
            : this(defaultTitle, scopeText, "保存查询", "保存当前查询语句", "保存")
        {
        }

        /// <summary>
        /// 初始化保存或重命名查询弹窗，并允许调用方指定文案。
        /// </summary>
        public SaveQueryDialog(string defaultTitle, string scopeText, string windowTitle, string headerText, string confirmText)
        {
            InitializeComponent();
            Title = string.IsNullOrWhiteSpace(windowTitle) ? "保存查询" : windowTitle;
            HeaderText.Text = string.IsNullOrWhiteSpace(headerText) ? "保存当前查询语句" : headerText;
            SaveButton.Content = string.IsNullOrWhiteSpace(confirmText) ? "保存" : confirmText;
            TitleBox.Text = defaultTitle ?? string.Empty;
            ScopeText.Text = scopeText ?? string.Empty;
            Loaded += SaveQueryDialog_Loaded;
        }

        /// <summary>
        /// 用户输入的查询标题。
        /// </summary>
        public string QueryTitle { get; private set; }

        /// <summary>
        /// 弹窗加载后聚焦标题输入框，方便直接输入。
        /// </summary>
        private void SaveQueryDialog_Loaded(object sender, RoutedEventArgs e)
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        }

        /// <summary>
        /// 标题输入框按回车时直接确认保存。
        /// </summary>
        private void TitleBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                ConfirmSave();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 取消保存。
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>
        /// 确认保存。
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSave();
        }

        /// <summary>
        /// 校验标题后关闭弹窗。
        /// </summary>
        private void ConfirmSave()
        {
            string title = (TitleBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                TXMessageBox.Show(this, "请输入查询名称。", "保存查询", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            QueryTitle = title;
            DialogResult = true;
        }
    }
}
