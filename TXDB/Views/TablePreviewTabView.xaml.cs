using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TX.WpfUI.Controls;
using TXDB.Models;
using TXDB.Services;

namespace TXDB.Views
{
    /// <summary>
    /// 表数据预览标签页，只展示分页结果，不包含 SQL 编辑器。
    /// </summary>
    public partial class TablePreviewTabView : UserControl
    {
        private readonly IList<int> pageSizeOptions;
        private readonly ObservableCollection<TableFilterCondition> filterConditions;
        private readonly ObservableCollection<string> filterColumnOptions;
        private readonly IList<string> filterLogicOptions;
        private readonly IList<TableFilterOperatorOption> filterOperatorOptions;
        private DataTable resultTable;
        private bool isUpdatingPagination;
        private bool isLoading;
        private int totalRows;
        private TextBox activeCellEditor;
        private string activeCellOriginalText;
        private bool hasUncommittedCellValue;
        /// <summary>
        /// 请求主窗口加载当前页数据。
        /// </summary>
        public event EventHandler<TablePageRequestedEventArgs> PageRequested;

        public event EventHandler<TableCommitRequestedEventArgs> CommitRequested;

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
        /// 界面绑定的筛选条件集合。
        /// </summary>
        public ObservableCollection<TableFilterCondition> FilterConditions
        {
            get { return filterConditions; }
        }

        /// <summary>
        /// 界面绑定的筛选字段下拉列表。
        /// </summary>
        public ObservableCollection<string> FilterColumnOptions
        {
            get { return filterColumnOptions; }
        }

        /// <summary>
        /// 界面绑定的 AND/OR 逻辑下拉列表。
        /// </summary>
        public IList<string> FilterLogicOptions
        {
            get { return filterLogicOptions; }
        }

        /// <summary>
        /// 界面绑定的筛选运算符下拉列表。
        /// </summary>
        public IList<TableFilterOperatorOption> FilterOperatorOptions
        {
            get { return filterOperatorOptions; }
        }

        /// <summary>
        /// 初始化表预览页。
        /// </summary>
        public TablePreviewTabView()
        {
            filterConditions = new ObservableCollection<TableFilterCondition>();
            filterColumnOptions = new ObservableCollection<string>();
            filterLogicOptions = new List<string> { "AND", "OR" };
            filterOperatorOptions = BuildFilterOperatorOptions();
            InitializeComponent();
            pageSizeOptions = new List<int> { 100, 200, 500, 1000, 10000 };
            PageIndex = 1;
            PageSize = 100;
            totalRows = 0;
            ResultGridHelper.Configure(ResultGrid);
            EnsureDefaultFilterCondition();
            UpdateFilterSummary();
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
            bool canEdit = tableNode != null && tableNode.Kind == DatabaseObjectKind.Table;
            ResultGrid.IsReadOnly = !canEdit;
            if (canEdit)
            {
                ResultGridHelper.Configure(ResultGrid);
            }
            else
            {
                ResultGridHelper.ConfigureQueryResult(ResultGrid);
            }

            AddRowButton.IsEnabled = canEdit;
            DeleteRowButton.IsEnabled = canEdit;
            UpdateEditCommandState();
            RefreshFilterColumnOptions();
            ResetInvalidFilterColumns();
            UpdateFilterSummary();
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
            IList<TableFilterCondition> activeFilters = GetActiveFilters();
            MessageText.Text = "正在加载第 " + PageIndex + " 页" + (activeFilters.Count > 0 ? "，已启用筛选..." : "...");
            EventHandler<TablePageRequestedEventArgs> handler = PageRequested;
            if (handler == null)
            {
                SetLoading(false);
                MessageText.Text = "没有可用的数据加载处理器。";
                return;
            }

            handler(this, new TablePageRequestedEventArgs(Profile.Clone(), TableNode, PageIndex, PageSize, activeFilters));
        }

        /// <summary>
        /// 设置当前页结果。
        /// </summary>
        public void SetResult(QueryExecutionResult result)
        {
            if (resultTable != null)
            {
                resultTable.ColumnChanged -= ResultTable_ColumnChanged;
                resultTable.RowChanged -= ResultTable_RowChanged;
                resultTable.RowDeleted -= ResultTable_RowDeleted;
            }

            resultTable = result == null ? new DataTable() : result.Table;
            if (resultTable != null)
            {
                resultTable.AcceptChanges();
                resultTable.ColumnChanged += ResultTable_ColumnChanged;
                resultTable.RowChanged += ResultTable_RowChanged;
                resultTable.RowDeleted += ResultTable_RowDeleted;
            }

            ResultGrid.ItemsSource = resultTable == null ? null : resultTable.DefaultView;
            int rowCount = resultTable == null ? 0 : resultTable.Rows.Count;
            totalRows = result != null && result.TotalRows >= 0 ? result.TotalRows : rowCount;
            MessageText.Text = result == null ? "无执行结果" : result.Message + " 耗时：" + result.Duration.TotalMilliseconds.ToString("0") + " ms";
            SetLoading(false);
            UpdateFilterSummary();
            UpdatePageState();
            UpdateSelectionStatus();
            UpdateEditCommandState();
        }

        /// <summary>
        /// 设置底部状态消息。
        /// </summary>
        public void SetMessage(string message)
        {
            MessageText.Text = message ?? string.Empty;
            SetLoading(false);
            UpdateFilterSummary();
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
            dialog.Filter = ResultExportHelper.FileFilter;
            dialog.DefaultExt = ".csv";
            dialog.AddExtension = true;
            dialog.FileName = BuildSafeFileName(TableNode == null ? "table-preview" : TableNode.Name) + "-page-" + PageIndex;
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            ResultExportHelper.Export(resultTable.DefaultView, dialog.FileName, dialog.FilterIndex);
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

            if (HasPendingChanges())
            {
                UpdatePageState();
                MessageBox.Show(Window.GetWindow(this), "当前页有未提交的修改，请先提交后再翻页。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
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

            if (HasPendingChanges())
            {
                UpdatePageState();
                MessageBox.Show(Window.GetWindow(this), "当前页有未提交的修改，请先提交后再更改每页行数。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
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
            if (HasPendingChanges())
            {
                MessageBox.Show(Window.GetWindow(this), "当前页有未提交的修改，请先提交或取消后再刷新。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RequestPage();
        }

        /// <summary>
        /// 导出按钮。
        /// </summary>
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            ExportCurrentResult();
        }

        private void AddRowButton_Click(object sender, RoutedEventArgs e)
        {
            if (resultTable == null)
            {
                return;
            }

            DataRow row = resultTable.NewRow();
            resultTable.Rows.Add(row);
            ResultGrid.SelectedItem = ResultGrid.Items[ResultGrid.Items.Count - 1];
            ResultGrid.ScrollIntoView(ResultGrid.SelectedItem);
            MessageText.Text = "已添加新行，填写字段后点击提交。";
        }

        private void DeleteRowButton_Click(object sender, RoutedEventArgs e)
        {
            ResultGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ResultGrid.CommitEdit(DataGridEditingUnit.Row, true);

            List<DataRowView> selectedRows = ResultGrid.SelectedItems.Cast<object>()
                .OfType<DataRowView>()
                .ToList();
            if (selectedRows.Count == 0 && ResultGrid.CurrentItem is DataRowView)
            {
                selectedRows.Add((DataRowView)ResultGrid.CurrentItem);
            }

            if (selectedRows.Count == 0)
            {
                MessageBox.Show(Window.GetWindow(this), "请先选择需要删除的行。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                Window.GetWindow(this),
                "确认删除选中的 " + selectedRows.Count + " 行，并提交当前页所有未提交更改吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            foreach (DataRowView row in selectedRows)
            {
                row.Delete();
            }

            MessageText.Text = "正在提交删除和当前页更改...";
            SubmitChanges();
        }

        private void CommitChangesButton_Click(object sender, RoutedEventArgs e)
        {
            SubmitChanges();
        }

        private void SubmitChanges()
        {
            if (ResultGrid.CommitEdit(DataGridEditingUnit.Cell, true))
            {
                ResultGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            ClearActiveCellEditTracking();

            DataTable changes = resultTable == null ? null : resultTable.GetChanges();
            if (changes == null || changes.Rows.Count == 0)
            {
                MessageText.Text = "没有待提交的修改。";
                return;
            }

            EventHandler<TableCommitRequestedEventArgs> handler = CommitRequested;
            if (handler == null)
            {
                MessageText.Text = "没有可用的数据提交处理器。";
                return;
            }

            SetLoading(true);
            MessageText.Text = "正在提交行变更...";
            handler(this, new TableCommitRequestedEventArgs(Profile.Clone(), TableNode, changes));
        }

        private void CancelChangesButton_Click(object sender, RoutedEventArgs e)
        {
            ResultGrid.CancelEdit(DataGridEditingUnit.Cell);
            ResultGrid.CancelEdit(DataGridEditingUnit.Row);
            ClearActiveCellEditTracking();
            if (resultTable != null)
            {
                resultTable.RejectChanges();
            }

            MessageText.Text = "已取消当前页未提交的更改。";
            UpdateSelectionStatus();
            UpdateEditCommandState();
        }

        private void ResultTable_RowChanged(object sender, DataRowChangeEventArgs e)
        {
            UpdateEditCommandState();
        }

        private void ResultTable_ColumnChanged(object sender, DataColumnChangeEventArgs e)
        {
            UpdateEditCommandState();
        }

        private void ResultTable_RowDeleted(object sender, DataRowChangeEventArgs e)
        {
            UpdateEditCommandState();
        }

        private void ResultGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount > 1)
            {
                return;
            }

            DataGridCell cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell == null || ResultGrid.IsReadOnly)
            {
                return;
            }

            if (cell.IsEditing)
            {
                return;
            }

            BeginCellEdit(cell, false);
        }

        private void ResultGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DataGridCell cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell == null || ResultGrid.IsReadOnly)
            {
                return;
            }

            e.Handled = true;
            BeginCellEdit(cell, true);
        }

        private void BeginCellEdit(DataGridCell cell, bool selectAll)
        {
            object rowItem = cell.DataContext;
            DataGridColumn column = cell.Column;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!ResultGrid.IsEnabled || rowItem == null)
                {
                    return;
                }

                ResultGrid.CurrentCell = new DataGridCellInfo(rowItem, column);
                ResultGrid.BeginEdit();
                TextBox editor = FindDescendant<TextBox>(cell);
                if (editor == null)
                {
                    return;
                }

                editor.Focus();
                if (selectAll)
                {
                    editor.SelectAll();
                }
                else
                {
                    editor.CaretIndex = editor.Text == null ? 0 : editor.Text.Length;
                }
            }));
        }

        private void ResultGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            TextBox editor = FindDescendant<TextBox>(e.EditingElement);
            if (editor == null)
            {
                return;
            }

            ClearActiveCellEditTracking();
            activeCellEditor = editor;
            activeCellOriginalText = editor.Text;
            activeCellEditor.TextChanged += ActiveCellEditor_TextChanged;
        }

        private void ResultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            TextBox endingEditor = FindDescendant<TextBox>(e.EditingElement);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (ReferenceEquals(activeCellEditor, endingEditor))
                {
                    ClearActiveCellEditTracking();
                }

                UpdateEditCommandState();
            }));
        }

        private void ActiveCellEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            hasUncommittedCellValue = activeCellEditor != null &&
                !string.Equals(activeCellEditor.Text, activeCellOriginalText, StringComparison.Ordinal);
            UpdateEditCommandState();
        }

        private void ClearActiveCellEditTracking()
        {
            if (activeCellEditor != null)
            {
                activeCellEditor.TextChanged -= ActiveCellEditor_TextChanged;
            }

            activeCellEditor = null;
            activeCellOriginalText = null;
            hasUncommittedCellValue = false;
        }

        private static T FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            DependencyObject current = element;
            while (current != null)
            {
                T match = current as T;
                if (match != null)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static T FindDescendant<T>(DependencyObject element) where T : DependencyObject
        {
            if (element == null)
            {
                return null;
            }

            T elementMatch = element as T;
            if (elementMatch != null)
            {
                return elementMatch;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(element, index);
                T match = child as T;
                if (match != null)
                {
                    return match;
                }

                match = FindDescendant<T>(child);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        public void SetCommitFailure(string message)
        {
            SetLoading(false);
            MessageText.Text = message ?? "提交失败。";
        }

        private bool HasPendingChanges()
        {
            return hasUncommittedCellValue || (resultTable != null && resultTable.GetChanges() != null);
        }

        private void UpdateEditCommandState()
        {
            bool hasPendingChanges = HasPendingChanges();
            bool canEdit = !isLoading && TableNode != null && TableNode.Kind == DatabaseObjectKind.Table;
            CommitChangesButton.IsEnabled = canEdit && hasPendingChanges;
            CancelChangesButton.IsEnabled = canEdit && hasPendingChanges;
        }

        /// <summary>
        /// 展开或收起快速筛选面板。
        /// </summary>
        private void FilterToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (FilterPanel.Visibility == Visibility.Visible)
            {
                CloseFilterPanelAndClearFilter();
                return;
            }

            FilterPanel.Visibility = Visibility.Visible;
            FilterToggleButton.Content = "关闭筛选";
        }

        /// <summary>
        /// 新增一条快速筛选条件。
        /// </summary>
        private void AddFilterButton_Click(object sender, RoutedEventArgs e)
        {
            filterConditions.Add(CreateDefaultFilterCondition());
            UpdateFilterSummary();
        }

        /// <summary>
        /// 应用当前快速筛选条件，并回到第一页重新加载数据。
        /// </summary>
        private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilterAndReload();
        }

        /// <summary>
        /// 清空快速筛选条件，并重新加载未筛选的数据。
        /// </summary>
        private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (HasPendingChanges())
            {
                MessageBox.Show(Window.GetWindow(this), "当前页有未提交的修改，请先提交或取消后再清空筛选。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ResetFilterConditions();
            ApplyFilterAndReload();
        }

        /// <summary>
        /// 删除当前筛选条件行。
        /// </summary>
        private void RemoveFilterButton_Click(object sender, RoutedEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            TableFilterCondition condition = element == null ? null : element.Tag as TableFilterCondition;
            if (condition != null)
            {
                filterConditions.Remove(condition);
            }

            EnsureDefaultFilterCondition();
            UpdateFilterSummary();
        }

        /// <summary>
        /// 在筛选值输入框中按回车时直接应用筛选。
        /// </summary>
        private void FilterValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Return)
            {
                return;
            }

            e.Handled = true;
            ApplyFilterAndReload();
        }

        /// <summary>
        /// 应用筛选并回到第一页重新请求数据。
        /// </summary>
        private void ApplyFilterAndReload()
        {
            if (isLoading)
            {
                return;
            }

            if (HasPendingChanges())
            {
                MessageBox.Show(Window.GetWindow(this), "当前页有未提交的修改，请先提交后再重新筛选。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            PageIndex = 1;
            UpdateFilterSummary();
            UpdatePageState();
            RequestPage();
        }

        /// <summary>
        /// 关闭筛选面板时同步取消筛选，避免只是隐藏界面但数据仍处于筛选状态。
        /// </summary>
        private void CloseFilterPanelAndClearFilter()
        {
            bool needReload = GetActiveFilters().Count > 0;
            if (needReload && HasPendingChanges())
            {
                MessageBox.Show(Window.GetWindow(this), "当前页有未提交的修改，请先提交或取消后再关闭筛选。", "TXDB", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            FilterPanel.Visibility = Visibility.Collapsed;
            FilterToggleButton.Content = "筛选";
            ResetFilterConditions();
            UpdateFilterSummary();
            if (needReload)
            {
                ApplyFilterAndReload();
            }
        }

        /// <summary>
        /// 重置快速筛选条件到一条默认空白条件。
        /// </summary>
        private void ResetFilterConditions()
        {
            filterConditions.Clear();
            EnsureDefaultFilterCondition();
        }

        /// <summary>
        /// 设置加载状态，避免重复翻页请求。
        /// </summary>
        private void SetLoading(bool loading)
        {
            isLoading = loading;
            PaginationControl.IsEnabled = !loading;
            FilterToggleButton.IsEnabled = !loading;
            FilterPanel.IsEnabled = !loading;
            ResultGrid.IsEnabled = !loading;
            AddRowButton.IsEnabled = !loading && TableNode != null && TableNode.Kind == DatabaseObjectKind.Table;
            DeleteRowButton.IsEnabled = !loading && TableNode != null && TableNode.Kind == DatabaseObjectKind.Table;
            UpdateEditCommandState();
            RefreshButton.IsEnabled = !loading;
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
        /// 根据当前表元数据刷新筛选字段下拉选项。
        /// </summary>
        private void RefreshFilterColumnOptions()
        {
            filterColumnOptions.Clear();
            if (TableNode == null)
            {
                return;
            }

            foreach (DatabaseObjectNode child in TableNode.Children)
            {
                if (child.Kind == DatabaseObjectKind.Column &&
                    !string.IsNullOrWhiteSpace(child.Name) &&
                    !FilterColumnExists(child.Name))
                {
                    filterColumnOptions.Add(child.Name);
                }
            }
        }

        /// <summary>
        /// 确保筛选条件至少保留一条空白行，便于用户直接编辑。
        /// </summary>
        private void EnsureDefaultFilterCondition()
        {
            if (filterConditions.Count == 0)
            {
                filterConditions.Add(CreateDefaultFilterCondition());
            }
        }

        /// <summary>
        /// 创建一条默认筛选条件。
        /// </summary>
        private TableFilterCondition CreateDefaultFilterCondition()
        {
            return new TableFilterCondition
            {
                IsEnabled = true,
                Logic = "AND",
                ColumnName = filterColumnOptions.Count > 0 ? filterColumnOptions[0] : string.Empty,
                OperatorCode = "Contains",
                Value = string.Empty
            };
        }

        /// <summary>
        /// 当前表字段变化后重置不存在的筛选字段。
        /// </summary>
        private void ResetInvalidFilterColumns()
        {
            foreach (TableFilterCondition condition in filterConditions)
            {
                if (!IsKnownFilterColumn(condition.ColumnName))
                {
                    condition.ColumnName = filterColumnOptions.Count > 0 ? filterColumnOptions[0] : string.Empty;
                }
            }
        }

        /// <summary>
        /// 获取当前可以真正下发到数据库的活动筛选条件。
        /// </summary>
        private IList<TableFilterCondition> GetActiveFilters()
        {
            List<TableFilterCondition> activeFilters = new List<TableFilterCondition>();
            foreach (TableFilterCondition condition in filterConditions)
            {
                if (condition == null || !condition.IsEnabled || !IsKnownFilterColumn(condition.ColumnName))
                {
                    continue;
                }

                if (IsFilterValueRequired(condition.OperatorCode) && string.IsNullOrWhiteSpace(condition.Value))
                {
                    continue;
                }

                activeFilters.Add(condition.Clone());
            }

            return activeFilters;
        }

        /// <summary>
        /// 刷新顶部筛选状态文本。
        /// </summary>
        private void UpdateFilterSummary()
        {
            int activeCount = GetActiveFilters().Count;
            FilterSummaryText.Text = activeCount <= 0 ? string.Empty : "已筛选 " + activeCount + " 条";
        }

        /// <summary>
        /// 判断筛选列下拉中是否已经存在指定字段。
        /// </summary>
        private bool FilterColumnExists(string columnName)
        {
            foreach (string item in filterColumnOptions)
            {
                if (SameName(item, columnName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判断当前筛选字段是否属于当前表字段。
        /// </summary>
        private bool IsKnownFilterColumn(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return false;
            }

            foreach (string item in filterColumnOptions)
            {
                if (SameName(item, columnName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判断当前运算符是否必须输入筛选值。
        /// </summary>
        private static bool IsFilterValueRequired(string operatorCode)
        {
            return !SameName(operatorCode, "IsNull") &&
                   !SameName(operatorCode, "IsNotNull") &&
                   !SameName(operatorCode, "IsEmpty") &&
                   !SameName(operatorCode, "IsNotEmpty");
        }

        /// <summary>
        /// 构造快速筛选运算符列表。
        /// </summary>
        private static IList<TableFilterOperatorOption> BuildFilterOperatorOptions()
        {
            return new List<TableFilterOperatorOption>
            {
                new TableFilterOperatorOption { OperatorCode = "Equal", DisplayName = "=" },
                new TableFilterOperatorOption { OperatorCode = "NotEqual", DisplayName = "!=" },
                new TableFilterOperatorOption { OperatorCode = "LessThan", DisplayName = "<" },
                new TableFilterOperatorOption { OperatorCode = "LessThanOrEqual", DisplayName = "<=" },
                new TableFilterOperatorOption { OperatorCode = "GreaterThan", DisplayName = ">" },
                new TableFilterOperatorOption { OperatorCode = "GreaterThanOrEqual", DisplayName = ">=" },
                new TableFilterOperatorOption { OperatorCode = "Contains", DisplayName = "包含" },
                new TableFilterOperatorOption { OperatorCode = "NotContains", DisplayName = "不包含" },
                new TableFilterOperatorOption { OperatorCode = "StartsWith", DisplayName = "开始以" },
                new TableFilterOperatorOption { OperatorCode = "NotStartsWith", DisplayName = "开始不是以" },
                new TableFilterOperatorOption { OperatorCode = "EndsWith", DisplayName = "结束以" },
                new TableFilterOperatorOption { OperatorCode = "NotEndsWith", DisplayName = "结束不是以" },
                new TableFilterOperatorOption { OperatorCode = "IsNull", DisplayName = "是 null" },
                new TableFilterOperatorOption { OperatorCode = "IsNotNull", DisplayName = "不是 null" },
                new TableFilterOperatorOption { OperatorCode = "IsEmpty", DisplayName = "是空的" },
                new TableFilterOperatorOption { OperatorCode = "IsNotEmpty", DisplayName = "不是空的" },
                new TableFilterOperatorOption { OperatorCode = "Between", DisplayName = "介于" },
                new TableFilterOperatorOption { OperatorCode = "In", DisplayName = "在列表" },
                new TableFilterOperatorOption { OperatorCode = "NotIn", DisplayName = "不在列表" }
            };
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
