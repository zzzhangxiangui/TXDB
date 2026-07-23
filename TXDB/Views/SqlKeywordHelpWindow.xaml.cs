using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TXDB.Models;
using TXDB.Services;

namespace TXDB.Views
{
    /// <summary>
    /// SQL 关键字帮助弹窗，左侧列出关键字，右侧展示说明和示例。
    /// </summary>
    public partial class SqlKeywordHelpWindow : Window
    {
        private readonly IList<SqlKeywordHelpInfo> keywordItems;

        /// <summary>
        /// 初始化 SQL 关键字帮助弹窗。
        /// </summary>
        public SqlKeywordHelpWindow()
        {
            InitializeComponent();
            keywordItems = SqlHelperCatalog.GetKeywordHelpItems();
            KeywordList.ItemsSource = keywordItems;
            KeywordList.SelectedItem = keywordItems.FirstOrDefault();
        }

        /// <summary>
        /// 切换关键字时刷新右侧说明内容。
        /// </summary>
        private void KeywordList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SqlKeywordHelpInfo item = KeywordList.SelectedItem as SqlKeywordHelpInfo;
            ShowKeyword(item);
        }

        /// <summary>
        /// 关闭帮助弹窗。
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 把关键字说明写入右侧详情区域。
        /// </summary>
        private void ShowKeyword(SqlKeywordHelpInfo item)
        {
            if (item == null)
            {
                TitleText.Text = string.Empty;
                DescriptionText.Text = string.Empty;
                SyntaxText.Text = string.Empty;
                ExampleText.Text = string.Empty;
                NotesText.Text = string.Empty;
                return;
            }

            TitleText.Text = item.Keyword + "  " + item.ChineseName;
            DescriptionText.Text = item.Description;
            SyntaxText.Text = item.Syntax;
            ExampleText.Text = item.Example;
            NotesText.Text = item.Notes;
        }
    }
}
