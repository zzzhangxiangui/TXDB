using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TX.WpfUI.Controls;
using TXDB.Models;

namespace TXDB.Views
{
    /// <summary>
    /// 表数据预览标签页，只展示分页结果，不包含 SQL 编辑器。
    /// </summary>
    public partial class TablePreviewTabView : UserControl
    {
        private readonly IList<int> pageSizeOptions;
        private DataTable resultTable;
        private bool isUpdatingPagination;
        private bool isLoading;
        private int totalRows;

        /// <summary>
        /// 请求主窗口加载当前页数据。
        /// </summary>
        public event EventHandler<TablePageRequestedEventArgs> PageRequested;

        /// <summary>
        /// 当前表预览页使用的连接配置。
        /// </summary>
        public ConnectionProfile Profile { get; private set; }

        /// <summary>
        /// 当前预览的表或视图节点。
        /// </summary>
        public DatabaseObjectNode TableNode { get; private set; }

        /// <summary>
        /// 当前页码，从 1 开始。
        /// </summary>
        public int PageIndex { get; private set; }

        /// <summary>
        /// 每页显示行数。
        /// </summary>
        public int PageSize { get; private set; }

        /// <summary>
        /// 初始化表预览页。
        /// </summary>
        public TablePreviewTabView()
        {
            InitializeComponent();
            pageSizeOptions = new List<int> { 100, 200, 500, 1000, 10000 };
            PageIndex = 1;
            PageSize = 100;
            totalRows = 0;
            ResultGridHelper.Configure(ResultGrid);
            InitializePaginationControl();
        }

        /// <summary>
        /// 初始化当前表预览上下文。
        /// </summary>
        public void Initialize(ConnectionProfile profile, DatabaseObjectNode tableNode)
        {
            Profile = profile == null ? null : profile.Clone();
            TableNode = tableNode;
            PageIndex = 1;
            totalRows = 0;
            TableNameText.Text = BuildTableDisplayName(tableNode);
            UpdatePageState();
        }

        /// <summary>
        /// 请求加载当前页。
        /// </summary>
        public void RequestPage()
        {
            if (isLoading || Profile == null || TableNode == null)
            {
                return;
            }

            SetLoading(true);
            MessageText.Text = "正在加载第 " + PageIndex + " 页...";
            EventHandler<TablePageRequestedEventArgs> handler = PageRequested;
            if (handler == null)
            {
                SetLoading(false);
                MessageText.Text = "没有可用的数据加载处理器。";
                return;
            }

            handler(this, new TablePageRequestedEventArgs(Profile.Clone(), TableNode, PageIndex, PageSize));
        }

        /// <summary>
        /// 设置当前页结果。
        /// </summary>
        public void SetResult(QueryExecutionResult result)
        {
            resultTable = result == null ? new DataTable() : result.Table;
            ResultGrid.ItemsSource = resultTable == null ? null : resultTable.DefaultView;
            int rowCount = resultTable == null ? 0 : resultTable.Rows.Count;
            totalRows = result != null && result.TotalRows >= 0 ? result.TotalRows : rowCount;
            MessageText.Text = result == null ? "无执行结果" : result.Message + " 耗时：" + result.Duration.TotalMilliseconds.ToString("0") + " ms";
            SetLoading(false);
            UpdatePageState();
            UpdateSelectionStatus();
        }

        /// <summary>
        /// 设置底部状态消息。
        /// </summary>
        public void SetMessage(string message)
        {
            MessageText.Text = message ?? string.Empty;
            SetLoading(false);
            UpdatePageState();
            UpdateSelectionStatus();
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
        /// 导出当前页结果。
        /// </summary>
        public void ExportCurrentResult()
        {
            if (resultTable == null || resultTable.Columns.Count == 0)
            {
                MessageBox.Show("当前没有可导出的结果。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "导出表预览结果";
            dialog.Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
            dialog.FileName = BuildSafeFileName(TableNode == null ? "table-preview" : TableNode.Name) + "-page-" + PageIndex + ".csv";
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            ExportCsv(dialog.FileName);
            MessageText.Text = "已导出：" + dialog.FileName;
        }

        /// <summary>
        /// 初始化分页控件，并监听页码和页大小变化。
        /// </summary>
        private void InitializePaginationControl()
        {
            isUpdatingPagination = true;
            try
            {
                PaginationControl.PageSizeOptions = pageSizeOptions;
                PaginationControl.PageSize = PageSize;
                PaginationControl.Total = 0;
                PaginationControl.PageIndex = PageIndex;
            }
            finally
            {
                isUpdatingPagination = false;
            }

            DependencyPropertyDescriptor pageIndexDescriptor = DependencyPropertyDescriptor.FromProperty(TXPagination.PageIndexProperty, typeof(TXPagination));
            if (pageIndexDescriptor != null)
            {
                pageIndexDescriptor.AddValueChanged(PaginationControl, PaginationControl_PageIndexChanged);
            }

            DependencyPropertyDescriptor pageSizeDescriptor = DependencyPropertyDescriptor.FromProperty(TXPagination.PageSizeProperty, typeof(TXPagination));
            if (pageSizeDescriptor != null)
            {
                pageSizeDescriptor.AddValueChanged(PaginationControl, PaginationControl_PageSizeChanged);
            }
        }

        /// <summary>
        /// 分页控件页码变化后加载目标页。
        /// </summary>
        private void PaginationControl_PageIndexChanged(object sender, EventArgs e)
        {
            if (isUpdatingPagination || isLoading)
            {
                return;
            }

            int targetPageIndex = Math.Max(1, PaginationControl.PageIndex);
            if (targetPageIndex == PageIndex)
            {
                return;
            }

            PageIndex = targetPageIndex;
            RequestPage();
        }

        /// <summary>
        /// 分页控件页大小变化后回到第一页重新加载。
        /// </summary>
        private void PaginationControl_PageSizeChanged(object sender, EventArgs e)
        {
            if (isUpdatingPagination || isLoading)
            {
                return;
            }

            int targetPageSize = Math.Max(1, PaginationControl.PageSize);
            if (targetPageSize == PageSize)
            {
                return;
            }

            PageSize = targetPageSize;
            PageIndex = 1;
            UpdatePageState();
            RequestPage();
        }

        /// <summary>
        /// 刷新当前页。
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RequestPage();
        }

        /// <summary>
        /// 导出按钮。
        /// </summary>
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            ExportCurrentResult();
        }

        /// <summary>
        /// 设置加载状态，避免重复翻页请求。
        /// </summary>
        private void SetLoading(bool loading)
        {
            isLoading = loading;
            PaginationControl.IsEnabled = !loading;
        }

        /// <summary>
        /// 根据真实总行数刷新分页控件状态。
        /// </summary>
        private void UpdatePageState()
        {
            isUpdatingPagination = true;
            try
            {
                PaginationControl.PageSizeOptions = pageSizeOptions;
                PaginationControl.PageSize = PageSize;
                PaginationControl.Total = Math.Max(0, totalRows);
                PaginationControl.PageIndex = PageIndex;
            }
            finally
            {
                isUpdatingPagination = false;
            }
        }

        /// <summary>
        /// 刷新当前表格选中行和总行数提示。
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

            DatabaseObjectNode metadataColumn = FindTableColumn(columnName);
            if (metadataColumn != null && !string.IsNullOrWhiteSpace(metadataColumn.DataType))
            {
                typeName = metadataColumn.DataType;
            }

            string comment = metadataColumn == null ? string.Empty : metadataColumn.Comment;
            return FormatFieldInfo(columnName, typeName, comment);
        }

        /// <summary>
        /// 从当前表元数据中查找选中的字段。
        /// </summary>
        private DatabaseObjectNode FindTableColumn(string columnName)
        {
            if (TableNode == null || string.IsNullOrWhiteSpace(columnName))
            {
                return null;
            }

            foreach (DatabaseObjectNode child in TableNode.Children)
            {
                if (child.Kind == DatabaseObjectKind.Column && SameName(child.Name, columnName))
                {
                    return child;
                }
            }

            return null;
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
        /// 构造表显示名称。
        /// </summary>
        private static string BuildTableDisplayName(DatabaseObjectNode tableNode)
        {
            if (tableNode == null)
            {
                return "表预览";
            }

            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(tableNode.DatabaseName))
            {
                parts.Add(tableNode.DatabaseName);
            }

            if (!string.IsNullOrWhiteSpace(tableNode.SchemaName))
            {
                parts.Add(tableNode.SchemaName);
            }

            parts.Add(tableNode.Name);
            return string.Join(".", parts.ToArray());
        }

        /// <summary>
        /// 将当前页结果导出为 CSV。
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
        /// 生成可用于文件名的表名。
        /// </summary>
        private static string BuildSafeFileName(string value)
        {
            string fileName = string.IsNullOrWhiteSpace(value) ? "table-preview" : value;
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '-');
            }

            return fileName;
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
