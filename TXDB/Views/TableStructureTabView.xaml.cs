using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TX.WpfUI.Controls;
using TXDB.Data;
using TXDB.Models;

namespace TXDB.Views
{
    public partial class TableStructureTabView : UserControl
    {
        private TableSchemaEditor editor;
        private TableSchemaSnapshot original;
        private bool busy = true;
        private bool needsRefresh;
        private List<TableColumnDefinition> newBaseline;
        public ConnectionProfile Profile { get; private set; }
        public DatabaseObjectNode TableNode { get; private set; }
        public bool IsNew { get; private set; }
        public ObservableCollection<TableColumnDefinition> Columns { get; private set; } = new ObservableCollection<TableColumnDefinition>();
        public ObservableCollection<string> TypeOptions { get; private set; } = new ObservableCollection<string>();
        public event EventHandler SchemaSaved;
        public event EventHandler SchemaSaving;
        public Func<bool> ConfirmBeforeSave { get; set; }
        public bool IsBusy { get { return busy; } }
        public bool HasPendingChanges
        {
            get
            {
                if (editor == null) return false;
                if (IsNew) return !string.IsNullOrWhiteSpace(NameBox.Text) || DatabaseBox.Text != (TableNode.DatabaseName ?? string.Empty) || SchemaBox.Text != (TableNode.SchemaName ?? string.Empty) ||
                    (newBaseline != null && (Columns.Count != newBaseline.Count || Columns.Where((c, i) => i >= newBaseline.Count || !c.SameDefinition(newBaseline[i])).Any()));
                return original != null && (Columns.Count != original.Columns.Count || Columns.Where((c, i) => i >= original.Columns.Count || !c.SameDefinition(original.Columns[i])).Any());
            }
        }

        public TableStructureTabView()
        {
            InitializeComponent();
            DataContext = this;
            Columns.CollectionChanged += (s, e) => UpdateButtons();
            UpdateButtons();
        }

        public async Task InitializeAsync(ConnectionProfile profile, DatabaseObjectNode node, bool isNew)
        {
            Profile = profile.Clone();
            TableNode = new DatabaseObjectNode { Name = node.Name, DatabaseName = node.DatabaseName, SchemaName = node.SchemaName, Kind = node.Kind };
            IsNew = isNew;
            editor = new TableSchemaEditor(DatabaseProviderRegistry.GetProvider(profile.ProviderType));
            foreach (string type in editor.GetTypeNames()) TypeOptions.Add(type);
            NameBox.Text = node.Name;
            DatabaseBox.Text = node.DatabaseName;
            SchemaBox.Text = node.SchemaName;
            NameBox.IsReadOnly = DatabaseBox.IsReadOnly = SchemaBox.IsReadOnly = !isNew;
            DatabaseBox.IsEnabled = profile.ProviderType == DatabaseProviderType.SqlServer || profile.ProviderType == DatabaseProviderType.MySQL;
            SchemaBox.IsEnabled = profile.ProviderType == DatabaseProviderType.SqlServer || profile.ProviderType == DatabaseProviderType.Oracle;
            CommentColumn.IsReadOnly = profile.ProviderType == DatabaseProviderType.SQLite;
            if (CommentColumn.IsReadOnly) CommentColumn.Header = "注释（SQLite 不支持）";
            StructureGrid.IsReadOnly = node.Kind != DatabaseObjectKind.Table;
            TitleText.Text = isNew ? "新建表" : node.Name + " 结构";
            if (isNew)
            {
                busy = false;
                AddColumn(true);
                newBaseline = Columns.Select(c => c.Clone()).ToList();
                MessageText.Text = "填写表名和字段后，点击提交创建表。";
                NameBox.Focus();
                UpdateButtons();
            }
            else await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            busy = true;
            UpdateButtons();
            MessageText.Text = "正在读取表结构…";
            try
            {
                TableSchemaSnapshot loaded = await Task.Run(() => editor.Load(Profile, TableNode));
                original = loaded;
                RestoreColumns();
                needsRefresh = false;
                MessageText.Text = TableNode.Kind == DatabaseObjectKind.View ? "视图结构仅供查看。" : "字段修改在提交后生效；文本默认值请使用单引号。";
            }
            catch (Exception ex)
            {
                needsRefresh = true;
                MessageText.Text = "读取结构失败：" + ex.Message;
                ShowError("读取表结构失败", ex);
            }
            finally { busy = false; UpdateButtons(); }
        }

        private void RestoreColumns()
        {
            foreach (TableColumnDefinition column in Columns) column.PropertyChanged -= Column_PropertyChanged;
            Columns.Clear();
            if (original == null) return;
            foreach (TableColumnDefinition column in original.Columns)
            {
                if (!TypeOptions.Contains(column.TypeName)) TypeOptions.Add(column.TypeName);
                TableColumnDefinition copy = column.Clone();
                copy.IsReadOnlyDefinition = TableNode.Kind != DatabaseObjectKind.Table;
                copy.PropertyChanged += Column_PropertyChanged;
                Columns.Add(copy);
            }
        }

        private void AddColumn(bool initial = false)
        {
            string type = Profile.ProviderType == DatabaseProviderType.Access ? "长整型" : Profile.ProviderType == DatabaseProviderType.SQLite ? "INTEGER" : Profile.ProviderType == DatabaseProviderType.Oracle ? "NUMBER" : "int";
            string name = initial ? "Id" : "字段";
            int index = 1;
            while (Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = "字段" + index++;
            TableColumnDefinition column = new TableColumnDefinition { Name = name, TypeName = type, IsPrimaryKey = initial, IsIdentity = initial && Profile.ProviderType != DatabaseProviderType.Oracle };
            column.PropertyChanged += Column_PropertyChanged;
            Columns.Add(column);
            StructureGrid.SelectedItem = column;
            StructureGrid.ScrollIntoView(column);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) { if (!FinishEdit()) return; AddColumn(); }
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!FinishEdit()) return;
            TableColumnDefinition column = StructureGrid.SelectedItem as TableColumnDefinition;
            if (column == null) return;
            column.PropertyChanged -= Column_PropertyChanged;
            Columns.Remove(column);
            MessageText.Text = "字段已从草稿移除，提交后生效。";
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (busy || needsRefresh || !FinishEdit() || !HasPendingChanges) return;
            if (ConfirmBeforeSave != null && !ConfirmBeforeSave()) return;
            DatabaseObjectNode target = new DatabaseObjectNode
            {
                Name = NameBox.Text.Trim(), DatabaseName = DatabaseBox.Text.Trim(), SchemaName = SchemaBox.Text.Trim(), Kind = DatabaseObjectKind.Table
            };
            if (Profile.ProviderType == DatabaseProviderType.MySQL) target.SchemaName = target.DatabaseName;
            if (Profile.ProviderType == DatabaseProviderType.SqlServer && target.SchemaName.Length == 0) target.SchemaName = "dbo";
            var draft = Columns.Select(c => c.Clone()).ToList();
            try { editor.Validate(target, draft, original); }
            catch (Exception ex) { ShowError("检查表结构", ex); return; }
            string removed = original == null ? string.Empty : string.Join("、", original.Columns.Where(c => !draft.Any(d => d.OriginalName == c.Name)).Select(c => c.Name));
            string confirmation = IsNew ? "确定创建表“" + target.Name + "”吗？" : "确定提交“" + target.Name + "”的结构更改吗？";
            if (removed.Length > 0) confirmation += "\n\n将删除字段：" + removed + "。这些字段中的数据也会删除。";
            if (original != null && draft.Any(c => original.Columns.Any(o => o.Name == c.OriginalName && (o.TypeName != c.TypeName || o.TypeArguments != c.TypeArguments)))) confirmation += "\n\n本次包含字段类型或长度更改，请确认已有数据能转换为新类型。";
            if (Profile.ProviderType == DatabaseProviderType.MySQL || Profile.ProviderType == DatabaseProviderType.Oracle) confirmation += "\n该数据库的结构更改会立即生效；如果中途失败，请刷新检查已完成的更改。";
            if (TXMessageBox.Show(Window.GetWindow(this), confirmation, "提交表结构", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (ConfirmBeforeSave != null && !ConfirmBeforeSave()) return;
            busy = true;
            UpdateButtons();
            EventHandler saving = SchemaSaving;
            if (saving != null) saving(this, EventArgs.Empty);
            MessageText.Text = "正在提交表结构…";
            bool saved = false;
            try
            {
                await Task.Run(() => editor.Save(Profile, target, original, draft));
                saved = true;
                TableNode = target;
                IsNew = false;
                NameBox.IsReadOnly = DatabaseBox.IsReadOnly = SchemaBox.IsReadOnly = true;
                TitleText.Text = target.Name + " 结构";
                SchemaBox.Text = target.SchemaName;
                EventHandler handler = SchemaSaved;
                if (handler != null) handler(this, EventArgs.Empty);
                original = await Task.Run(() => editor.Load(Profile, target));
                RestoreColumns();
                needsRefresh = false;
                MessageText.Text = "表结构已提交。";
            }
            catch (Exception ex)
            {
                needsRefresh = saved || Profile.ProviderType == DatabaseProviderType.MySQL || Profile.ProviderType == DatabaseProviderType.Oracle || Profile.ProviderType == DatabaseProviderType.Access;
                MessageText.Text = saved ? "结构已提交，但重新读取失败，请刷新。" : "提交失败：" + ex.Message;
                ShowError(saved ? "结构已提交，刷新失败" : "提交表结构失败", ex);
            }
            finally { busy = false; UpdateButtons(); }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscard()) return;
            StructureGrid.CancelEdit(DataGridEditingUnit.Cell);
            StructureGrid.CancelEdit(DataGridEditingUnit.Row);
            RestoreColumns();
            if (IsNew) ResetNewDraft();
            MessageText.Text = "已取消未提交的结构更改。";
            UpdateButtons();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscard()) return;
            StructureGrid.CancelEdit(DataGridEditingUnit.Cell);
            StructureGrid.CancelEdit(DataGridEditingUnit.Row);
            if (IsNew) { RestoreColumns(); ResetNewDraft(); MessageText.Text = "已重置新建表草稿。"; UpdateButtons(); }
            else await ReloadAsync();
        }

        public bool ConfirmClose()
        {
            if (busy) { TXMessageBox.Show(Window.GetWindow(this), "正在处理表结构，请完成后再关闭。", "表结构", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
            return ConfirmDiscard();
        }

        private bool ConfirmDiscard()
        {
            return !HasPendingChanges || TXMessageBox.Show(Window.GetWindow(this), "有未提交的表结构更改，确定放弃吗？", "表结构", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        private bool FinishEdit() { return StructureGrid.CommitEdit(DataGridEditingUnit.Cell, true) && StructureGrid.CommitEdit(DataGridEditingUnit.Row, true); }
        private void ResetNewDraft()
        {
            NameBox.Clear();
            DatabaseBox.Text = TableNode.DatabaseName;
            SchemaBox.Text = TableNode.SchemaName;
            AddColumn(true);
        }
        private void Column_PropertyChanged(object sender, PropertyChangedEventArgs e) { UpdateButtons(); }
        private void Definition_TextChanged(object sender, TextChangedEventArgs e) { UpdateButtons(); }
        private void StructureGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateButtons(); }

        private void UpdateButtons()
        {
            if (SaveButton == null) return;
            bool editable = !busy && !needsRefresh && TableNode != null && TableNode.Kind == DatabaseObjectKind.Table;
            AddButton.IsEnabled = editable;
            DeleteButton.IsEnabled = editable && StructureGrid.SelectedItem != null;
            SaveButton.IsEnabled = editable && HasPendingChanges && (!IsNew || !string.IsNullOrWhiteSpace(NameBox.Text));
            CancelButton.IsEnabled = !busy && HasPendingChanges;
            RefreshButton.IsEnabled = !busy && editor != null;
            StructureGrid.IsEnabled = !busy && !needsRefresh;
            ScopePanel.IsEnabled = !busy;
        }

        private void StructureGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridCell))
                source = source is FrameworkContentElement ? ((FrameworkContentElement)source).Parent : VisualTreeHelper.GetParent(source);
            DataGridCell cell = source as DataGridCell;
            TableColumnDefinition column = cell == null ? null : cell.DataContext as TableColumnDefinition;
            if (cell == null || cell.IsEditing || column == null || !column.CanEdit || StructureGrid.IsReadOnly || !(cell.Column is DataGridTextColumn)) return;
            StructureGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
            StructureGrid.SelectedItem = cell.DataContext;
            StructureGrid.BeginEdit();
            e.Handled = true;
        }

        private void StructureGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            TextBox box = e.EditingElement as TextBox;
            if (box == null) return;
            box.Focus();
            box.CaretIndex = box.Text.Length;
        }

        private void StructureGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            TableColumnDefinition column = e.Row.Item as TableColumnDefinition;
            if (column == null || !column.CanEdit) e.Cancel = true;
        }

        private void CellEditor_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            TextBox box = sender as TextBox;
            if (box != null) { box.SelectAll(); e.Handled = true; }
        }

        private void ShowError(string title, Exception ex) { TXMessageBox.Show(Window.GetWindow(this), ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error); }
        public void Cleanup()
        {
            foreach (TableColumnDefinition column in Columns) column.PropertyChanged -= Column_PropertyChanged;
            ConfirmBeforeSave = null;
        }
    }
}
