using System;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TX.WpfUI.Controls;

namespace TXDB.Views
{
    /// <summary>
    /// 结果表格显示辅助类，统一处理复制、列宽、长文本显示和单元格选择。
    /// </summary>
    public static class ResultGridHelper
    {
        private const double DefaultResultColumnWidth = 160d;
        private const double MaxResultColumnWidth = 720d;

        private static readonly DataTemplate ResultColumnHeaderTemplate = CreateResultColumnHeaderTemplate();
        private static readonly Style ResultTextElementStyle = CreateResultTextElementStyle();
        private static readonly Style QueryResultTextElementStyle = CreateQueryResultTextElementStyle();
        private static readonly DependencyProperty UseQueryCellFocusVisualsProperty =
            DependencyProperty.RegisterAttached("UseQueryCellFocusVisuals", typeof(bool), typeof(ResultGridHelper), new PropertyMetadata(false));

        /// <summary>
        /// 配置查询结果或表预览结果表格的通用交互。
        /// </summary>
        public static void Configure(TXDataGrid grid)
        {
            if (grid == null)
            {
                return;
            }

            grid.SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
            grid.SelectionMode = DataGridSelectionMode.Extended;
            grid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
            grid.CanUserResizeColumns = true;
            grid.CanUserReorderColumns = true;
            grid.CanUserSortColumns = false;
            grid.EnableRowVirtualization = true;
            grid.EnableColumnVirtualization = true;
            grid.ColumnWidth = new DataGridLength(DefaultResultColumnWidth);
            VirtualizingPanel.SetIsVirtualizing(grid, true);
            VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(grid, true);
            ScrollViewer.SetIsDeferredScrollingEnabled(grid, false);
            grid.AutoGeneratingColumn -= ResultGrid_AutoGeneratingColumn;
            grid.AutoGeneratingColumn += ResultGrid_AutoGeneratingColumn;
            grid.AutoGeneratingColumn -= QueryResultGrid_AutoGeneratingColumn;
            grid.Sorting -= ResultGrid_Sorting;
            grid.Sorting += ResultGrid_Sorting;
            grid.PreviewMouseRightButtonDown -= ResultGrid_PreviewMouseRightButtonDown;
            grid.PreviewMouseRightButtonDown += ResultGrid_PreviewMouseRightButtonDown;
            grid.PreviewMouseLeftButtonDown -= QueryResultGrid_PreviewMouseLeftButtonDown;
            grid.PreviewMouseDoubleClick -= QueryResultGrid_PreviewMouseDoubleClick;
            grid.ContextMenu = CreateContextMenu();
        }

        /// <summary>
        /// 配置查询结果网格，使只读单元格仍可定位和选择其中的文本。
        /// </summary>
        public static void ConfigureQueryResult(TXDataGrid grid, bool useQueryCellFocusVisuals = false)
        {
            if (grid == null)
            {
                return;
            }

            Configure(grid);
            grid.SetValue(UseQueryCellFocusVisualsProperty, useQueryCellFocusVisuals);
            if (useQueryCellFocusVisuals)
            {
                // The grid enters its native edit visual only to provide a caret; its editor stays read-only.
                grid.IsReadOnly = false;
            }
            grid.AutoGeneratingColumn -= ResultGrid_AutoGeneratingColumn;
            grid.AutoGeneratingColumn -= QueryResultGrid_AutoGeneratingColumn;
            grid.AutoGeneratingColumn += QueryResultGrid_AutoGeneratingColumn;
            grid.PreviewMouseLeftButtonDown -= QueryResultGrid_PreviewMouseLeftButtonDown;
            grid.PreviewMouseLeftButtonDown += QueryResultGrid_PreviewMouseLeftButtonDown;
            grid.PreviewMouseDoubleClick -= QueryResultGrid_PreviewMouseDoubleClick;
            grid.PreviewMouseDoubleClick += QueryResultGrid_PreviewMouseDoubleClick;
        }

        private static void QueryResultGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            TXDataGrid grid = sender as TXDataGrid;
            if (grid == null)
            {
                return;
            }

            bool useQueryCellFocusVisuals = (bool)grid.GetValue(UseQueryCellFocusVisualsProperty);
            if (useQueryCellFocusVisuals)
            {
                TXDataGridTextColumn textColumn = new TXDataGridTextColumn();
                textColumn.Header = e.PropertyName;
                textColumn.HeaderTemplate = ResultColumnHeaderTemplate;
                textColumn.SortMemberPath = e.PropertyName;
                textColumn.CanUserSort = false;
                textColumn.MinWidth = 70;
                textColumn.MaxWidth = MaxResultColumnWidth;
                textColumn.Width = new DataGridLength(DefaultResultColumnWidth);
                textColumn.ElementStyle = QueryResultTextElementStyle;
                textColumn.EditingElementStyle = CreateReadOnlyQueryEditorStyle(grid);
                textColumn.Binding = CreateCellBinding(e.PropertyName);
                textColumn.ClipboardContentBinding = CreateCellBinding(e.PropertyName);
                e.Column = textColumn;
                return;
            }

            DataGridTemplateColumn column = new DataGridTemplateColumn();
            column.Header = e.PropertyName;
            column.HeaderTemplate = ResultColumnHeaderTemplate;
            column.SortMemberPath = e.PropertyName;
            column.CanUserSort = false;
            column.MinWidth = 70;
            column.MaxWidth = MaxResultColumnWidth;
            column.Width = new DataGridLength(DefaultResultColumnWidth);
            column.IsReadOnly = true;
            column.ClipboardContentBinding = CreateCellBinding(e.PropertyName);
            column.CellTemplate = CreateReadOnlySelectableCellTemplate(e.PropertyName);
            e.Column = column;
        }

        private static DataTemplate CreateReadOnlySelectableCellTemplate(string columnName)
        {
            FrameworkElementFactory textBox = new FrameworkElementFactory(typeof(TextBox));
            textBox.SetBinding(TextBox.TextProperty, CreateCellBinding(columnName));
            textBox.SetValue(TextBox.IsReadOnlyProperty, true);
            textBox.SetValue(TextBox.IsReadOnlyCaretVisibleProperty, true);
            textBox.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
            textBox.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
            textBox.SetValue(TextBox.PaddingProperty, new Thickness(6, 0, 6, 0));
            textBox.SetValue(TextBox.TextWrappingProperty, TextWrapping.NoWrap);
            textBox.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            textBox.SetValue(TextBox.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            textBox.SetValue(TextBox.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);

            return new DataTemplate { VisualTree = textBox };
        }

        private static Style CreateQueryResultTextElementStyle()
        {
            Style style = new Style(typeof(TextBlock), ResultTextElementStyle);
            DataTrigger selectedTrigger = new DataTrigger
            {
                Binding = new Binding("IsSelected")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridCell), 1)
                },
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, SystemColors.HighlightTextBrush));
            style.Triggers.Add(selectedTrigger);
            return style;
        }

        private static Style CreateReadOnlyQueryEditorStyle(TXDataGrid grid)
        {
            Style baseStyle = grid.TryFindResource(typeof(TextBox)) as Style;
            Style style = baseStyle == null
                ? new Style(typeof(TextBox))
                : new Style(typeof(TextBox), baseStyle);
            style.Setters.Add(new Setter(TextBox.IsReadOnlyProperty, true));
            style.Setters.Add(new Setter(TextBox.IsReadOnlyCaretVisibleProperty, true));
            style.Setters.Add(new Setter(TextBox.BackgroundProperty, Brushes.White));
            style.Setters.Add(new Setter(TextBox.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(6, 0, 6, 0)));
            style.Setters.Add(new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            return style;
        }

        private static void QueryResultGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TXDataGrid grid = sender as TXDataGrid;
            if (grid != null && (bool)grid.GetValue(UseQueryCellFocusVisualsProperty))
            {
                DataGridCell cell = FindParent<DataGridCell>(e.OriginalSource as DependencyObject);
                if (cell == null || e.ClickCount > 1 || cell.IsEditing || Keyboard.Modifiers != ModifierKeys.None)
                {
                    return;
                }

                BeginQueryCellEdit(grid, cell, false);
                return;
            }

            TextBox textBox = FindParent<TextBox>(e.OriginalSource as DependencyObject);
            if (textBox == null || e.ClickCount > 1 || textBox.IsKeyboardFocusWithin)
            {
                return;
            }

            textBox.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!textBox.IsVisible)
                {
                    return;
                }

                textBox.Focus();
                textBox.CaretIndex = textBox.Text == null ? 0 : textBox.Text.Length;
            }));
        }

        private static void QueryResultGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            TXDataGrid grid = sender as TXDataGrid;
            if (grid != null && (bool)grid.GetValue(UseQueryCellFocusVisualsProperty))
            {
                DataGridCell cell = FindParent<DataGridCell>(e.OriginalSource as DependencyObject);
                if (cell == null)
                {
                    return;
                }

                e.Handled = true;
                BeginQueryCellEdit(grid, cell, true);
                return;
            }

            TextBox textBox = FindParent<TextBox>(e.OriginalSource as DependencyObject);
            if (textBox == null)
            {
                return;
            }

            e.Handled = true;
            textBox.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!textBox.IsVisible)
                {
                    return;
                }

                textBox.Focus();
                textBox.SelectAll();
            }));
        }

        private static void BeginQueryCellEdit(TXDataGrid grid, DataGridCell cell, bool selectAll)
        {
            object rowItem = cell.DataContext;
            DataGridColumn column = cell.Column;
            grid.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!grid.IsEnabled || rowItem == null)
                {
                    return;
                }

                grid.CurrentCell = new DataGridCellInfo(rowItem, column);
                grid.BeginEdit();
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

        /// <summary>
        /// 把自动生成的普通列替换为轻量文本列，避免模板列导致滚动卡顿。
        /// </summary>
        private static void ResultGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            TXDataGrid grid = sender as TXDataGrid;
            string columnName = e.PropertyName;
            TXDataGridTextColumn column = new TXDataGridTextColumn();
            column.Header = columnName;
            column.HeaderTemplate = ResultColumnHeaderTemplate;
            column.SortMemberPath = columnName;
            column.CanUserSort = false;
            column.MinWidth = 70;
            column.MaxWidth = MaxResultColumnWidth;
            column.Width = new DataGridLength(DefaultResultColumnWidth);
            column.ElementStyle = ResultTextElementStyle;
            column.Binding = CreateCellBinding(columnName, !grid.IsReadOnly);
            column.ClipboardContentBinding = CreateCellBinding(columnName);
            e.Column = column;
        }

        /// <summary>
        /// 创建结果单元格文本样式，保留省略显示但不再增加额外按钮控件。
        /// </summary>
        private static Style CreateResultTextElementStyle()
        {
            Style style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
            style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 0, 6, 0)));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
            return style;
        }

        /// <summary>
        /// 创建结果表格列头模板，避免 WPF 将下划线当成访问键处理。
        /// </summary>
        private static DataTemplate CreateResultColumnHeaderTemplate()
        {
            FrameworkElementFactory textBlock = new FrameworkElementFactory(typeof(TextBlock));
            textBlock.SetBinding(TextBlock.TextProperty, new Binding("."));
            textBlock.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            textBlock.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            textBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textBlock.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Left);
            return new DataTemplate { VisualTree = textBlock };
        }

        /// <summary>
        /// 创建从 DataRowView 按列名读取单元格值的轻量绑定。
        /// </summary>
        private static Binding CreateCellBinding(string columnName, bool allowEdit = false)
        {
            Binding binding = new Binding("[" + columnName + "]");
            binding.Mode = allowEdit ? BindingMode.TwoWay : BindingMode.OneWay;
            if (allowEdit)
            {
                binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
            }

            return binding;
        }

        /// <summary>
        /// 构造当前结果表格选中状态文本。
        /// </summary>
        public static string BuildSelectionStatus(TXDataGrid grid)
        {
            if (grid == null || grid.Items == null)
            {
                return "第 0 条 / 共 0 条";
            }

            int totalCount = grid.Items.Count;
            int currentIndex = grid.CurrentItem == null ? -1 : grid.Items.IndexOf(grid.CurrentItem);
            if (currentIndex < 0 || currentIndex >= totalCount)
            {
                return "第 0 条 / 共 " + totalCount + " 条";
            }

            int selectedCellCount = grid.SelectedCells == null ? 0 : grid.SelectedCells.Count;
            if (selectedCellCount > 1)
            {
                return "第 " + (currentIndex + 1) + " 条 / 共 " + totalCount + " 条，已选 " + selectedCellCount + " 格";
            }

            return "第 " + (currentIndex + 1) + " 条 / 共 " + totalCount + " 条";
        }

        /// <summary>
        /// 获取当前单元格对应的列名，供底部字段信息展示使用。
        /// </summary>
        public static string GetCurrentColumnName(TXDataGrid grid)
        {
            if (grid == null || !grid.CurrentCell.IsValid || grid.CurrentCell.Column == null)
            {
                return string.Empty;
            }

            return Convert.ToString(grid.CurrentCell.Column.Header, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        /// <summary>
        /// 拦截列头默认点击排序，排序只允许通过列头右键菜单显式触发。
        /// </summary>
        private static void ResultGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// 转义 DataView.Sort 需要的字段名，兼容空格、括号和特殊字符。
        /// </summary>
        private static string EscapeDataViewSortColumnName(string columnName)
        {
            return "[" + (columnName ?? string.Empty).Replace("\\", "\\\\").Replace("]", "\\]") + "]";
        }

        /// <summary>
        /// 创建结果表格右键菜单。
        /// </summary>
        private static ContextMenu CreateContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.Opened += ResultGridContextMenu_Opened;

            MenuItem copyItem = new MenuItem();
            copyItem.Header = "复制单元格";
            copyItem.Click += CopyCellMenuItem_Click;
            menu.Items.Add(copyItem);

            return menu;
        }

        /// <summary>
        /// 打开菜单前按照当前单元格状态更新菜单可用性。
        /// </summary>
        private static void ResultGridContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = sender as ContextMenu;
            if (menu == null)
            {
                return;
            }

            TXDataGrid grid = menu.PlacementTarget as TXDataGrid;
            bool hasCell = grid != null && grid.CurrentCell.IsValid;
            foreach (object item in menu.Items)
            {
                MenuItem menuItem = item as MenuItem;
                if (menuItem != null)
                {
                    menuItem.IsEnabled = hasCell;
                }
            }
        }

        /// <summary>
        /// 右键单元格时同步选中当前单元格，便于复制文本。
        /// </summary>
        private static void ResultGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            TXDataGrid grid = sender as TXDataGrid;
            DataGridColumnHeader header = FindParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject);
            if (grid != null && header != null && header.Column != null)
            {
                ShowColumnHeaderContextMenu(grid, header.Column);
                e.Handled = true;
                return;
            }

            DataGridCell cell = FindParent<DataGridCell>(e.OriginalSource as DependencyObject);
            if (grid == null || cell == null)
            {
                return;
            }

            DataGridRow row = FindParent<DataGridRow>(cell);
            if (row == null)
            {
                return;
            }

            grid.SelectedCells.Clear();
            grid.CurrentCell = new DataGridCellInfo(row.Item, cell.Column);
            grid.SelectedCells.Add(grid.CurrentCell);
            cell.Focus();
        }

        /// <summary>
        /// 显示列头右键排序菜单，把排序动作变成用户明确选择。
        /// </summary>
        private static void ShowColumnHeaderContextMenu(TXDataGrid grid, DataGridColumn column)
        {
            ContextMenu menu = new ContextMenu();
            SortMenuContext context = new SortMenuContext(grid, column);
            menu.Tag = context;
            menu.PlacementTarget = grid;
            menu.Placement = PlacementMode.MousePoint;

            MenuItem ascendingItem = CreateSortMenuItem("升序排序", ListSortDirection.Ascending);
            MenuItem descendingItem = CreateSortMenuItem("降序排序", ListSortDirection.Descending);
            MenuItem removeItem = CreateRemoveSortMenuItem();
            bool canSort = CanSortColumn(grid, column);
            ascendingItem.IsEnabled = canSort;
            descendingItem.IsEnabled = canSort;
            removeItem.IsEnabled = canSort && HasActiveSort(grid);

            menu.Items.Add(ascendingItem);
            menu.Items.Add(descendingItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(removeItem);
            menu.IsOpen = true;
        }

        /// <summary>
        /// 创建升序或降序排序菜单项。
        /// </summary>
        private static MenuItem CreateSortMenuItem(string header, ListSortDirection direction)
        {
            MenuItem item = new MenuItem();
            item.Header = header;
            item.Tag = direction;
            item.Click += SortColumnMenuItem_Click;
            return item;
        }

        /// <summary>
        /// 创建移除排序菜单项。
        /// </summary>
        private static MenuItem CreateRemoveSortMenuItem()
        {
            MenuItem item = new MenuItem();
            item.Header = "移除排序";
            item.Click += RemoveSortMenuItem_Click;
            return item;
        }

        /// <summary>
        /// 复制当前单元格文本。
        /// </summary>
        private static void CopyCellMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TXDataGrid grid = GetGridFromMenuItem(sender as MenuItem);
            if (grid == null || !grid.CurrentCell.IsValid)
            {
                return;
            }

            Clipboard.SetText(GetCurrentCellText(grid));
        }

        /// <summary>
        /// 执行列头右键菜单中的升序或降序排序。
        /// </summary>
        private static void SortColumnMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;
            SortMenuContext context = GetSortContextFromMenuItem(item);
            if (context == null || !(item.Tag is ListSortDirection))
            {
                return;
            }

            ApplyColumnSort(context.Grid, context.Column, (ListSortDirection)item.Tag);
        }

        /// <summary>
        /// 移除当前结果表格的排序，恢复原始结果顺序。
        /// </summary>
        private static void RemoveSortMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SortMenuContext context = GetSortContextFromMenuItem(sender as MenuItem);
            if (context == null)
            {
                return;
            }

            ApplyColumnSort(context.Grid, context.Column, null);
        }

        /// <summary>
        /// 对 DataView 应用排序或清除排序。
        /// </summary>
        private static void ApplyColumnSort(TXDataGrid grid, DataGridColumn column, ListSortDirection? direction)
        {
            DataView view = grid == null ? null : grid.ItemsSource as DataView;
            if (grid == null || view == null)
            {
                return;
            }

            try
            {
                foreach (DataGridColumn gridColumn in grid.Columns)
                {
                    gridColumn.SortDirection = null;
                }

                if (!direction.HasValue)
                {
                    view.Sort = string.Empty;
                    return;
                }

                string columnName = GetColumnHeaderText(column);
                if (string.IsNullOrWhiteSpace(columnName) || view.Table == null || !view.Table.Columns.Contains(columnName))
                {
                    return;
                }

                view.Sort = EscapeDataViewSortColumnName(columnName) + (direction.Value == ListSortDirection.Ascending ? " ASC" : " DESC");
                column.SortDirection = direction.Value;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(grid), "排序失败：" + ex.Message, "TXDB", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 判断当前列是否可以排序，避免非数据列或异常字段触发错误。
        /// </summary>
        private static bool CanSortColumn(TXDataGrid grid, DataGridColumn column)
        {
            DataView view = grid == null ? null : grid.ItemsSource as DataView;
            string columnName = GetColumnHeaderText(column);
            return view != null &&
                   view.Table != null &&
                   !string.IsNullOrWhiteSpace(columnName) &&
                   view.Table.Columns.Contains(columnName);
        }

        /// <summary>
        /// 判断当前表格是否已经存在排序。
        /// </summary>
        private static bool HasActiveSort(TXDataGrid grid)
        {
            DataView view = grid == null ? null : grid.ItemsSource as DataView;
            if (view != null && !string.IsNullOrWhiteSpace(view.Sort))
            {
                return true;
            }

            if (grid == null)
            {
                return false;
            }

            foreach (DataGridColumn column in grid.Columns)
            {
                if (column.SortDirection.HasValue)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 读取列头显示文本作为 DataTable 字段名。
        /// </summary>
        private static string GetColumnHeaderText(DataGridColumn column)
        {
            return column == null ? string.Empty : Convert.ToString(column.Header, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        /// <summary>
        /// 从菜单项反查所属结果表格。
        /// </summary>
        private static TXDataGrid GetGridFromMenuItem(MenuItem menuItem)
        {
            if (menuItem == null)
            {
                return null;
            }

            ContextMenu menu = menuItem.Parent as ContextMenu;
            return menu == null ? null : menu.PlacementTarget as TXDataGrid;
        }

        /// <summary>
        /// 从列头排序菜单项反查表格和列。
        /// </summary>
        private static SortMenuContext GetSortContextFromMenuItem(MenuItem menuItem)
        {
            if (menuItem == null)
            {
                return null;
            }

            ContextMenu menu = menuItem.Parent as ContextMenu;
            return menu == null ? null : menu.Tag as SortMenuContext;
        }

        /// <summary>
        /// 读取当前单元格文本。
        /// </summary>
        private static string GetCurrentCellText(TXDataGrid grid)
        {
            if (grid == null || !grid.CurrentCell.IsValid)
            {
                return string.Empty;
            }

            string columnName = Convert.ToString(grid.CurrentCell.Column.Header, CultureInfo.CurrentCulture);
            object rowObject = grid.CurrentCell.Item;
            DataRowView rowView = rowObject as DataRowView;
            if (rowView != null && rowView.DataView != null && rowView.DataView.Table != null && rowView.DataView.Table.Columns.Contains(columnName))
            {
                return NormalizeCellValue(rowView[columnName]);
            }

            DataRow row = rowObject as DataRow;
            if (row != null && row.Table != null && row.Table.Columns.Contains(columnName))
            {
                return NormalizeCellValue(row[columnName]);
            }

            return NormalizeCellValue(rowObject);
        }

        /// <summary>
        /// 把数据库空值和普通对象统一转换成显示文本。
        /// </summary>
        private static string NormalizeCellValue(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        private static T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                T typedChild = child as T;
                if (typedChild != null)
                {
                    return typedChild;
                }

                T descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        /// <summary>
        /// 从当前可视元素向上查找指定类型父级。
        /// </summary>
        private static T FindParent<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null)
            {
                T typed = source as T;
                if (typed != null)
                {
                    return typed;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        /// <summary>
        /// 列头右键排序菜单需要携带的上下文。
        /// </summary>
        private class SortMenuContext
        {
            /// <summary>
            /// 初始化排序菜单上下文。
            /// </summary>
            public SortMenuContext(TXDataGrid grid, DataGridColumn column)
            {
                Grid = grid;
                Column = column;
            }

            /// <summary>
            /// 当前结果表格。
            /// </summary>
            public TXDataGrid Grid { get; private set; }

            /// <summary>
            /// 当前右键点击的列。
            /// </summary>
            public DataGridColumn Column { get; private set; }
        }
    }
}
