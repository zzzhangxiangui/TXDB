using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using TXDB.Services;

namespace TXDB.Views
{
    /// <summary>
    /// 封装 AvalonEdit SQL 编辑器和自动提示交互。
    /// </summary>
    public partial class SqlEditorControl : UserControl
    {
        private CompletionWindow completionWindow;
        private SqlCompletionService completionService;

        // 语义着色器负责把元数据中的表名、字段名显示成不同颜色。
        private SqlSemanticColorizer semanticColorizer;

        /// <summary>
        /// 初始化 SQL 编辑控件。
        /// </summary>
        public SqlEditorControl()
        {
            InitializeComponent();
            completionService = new SqlCompletionService();
            semanticColorizer = new SqlSemanticColorizer(completionService);
            completionService.MetadataChanged += CompletionService_MetadataChanged;

            // 设置基础编辑体验，SQL 高亮定义如果存在就启用，不存在也不影响编辑器运行。
            Editor.Options.EnableHyperlinks = false;
            Editor.Options.EnableEmailHyperlinks = false;
            Editor.Options.HighlightCurrentLine = true;
            Editor.TextArea.TextEntered += TextArea_TextEntered;
            Editor.TextArea.TextEntering += TextArea_TextEntering;
            Editor.PreviewKeyDown += Editor_PreviewKeyDown;

            IHighlightingDefinition sqlHighlighting = HighlightingManager.Instance.GetDefinition("SQL");
            if (sqlHighlighting != null)
            {
                Editor.SyntaxHighlighting = sqlHighlighting;
            }

            // 在默认 SQL 高亮之上叠加元数据语义色，表名和字段名来自当前连接缓存。
            Editor.TextArea.TextView.LineTransformers.Add(semanticColorizer);
            Editor.Text = string.Empty;
        }

        /// <summary>
        /// 获取或设置自动提示服务。
        /// </summary>
        public SqlCompletionService CompletionService
        {
            get { return completionService; }
            set
            {
                completionService.MetadataChanged -= CompletionService_MetadataChanged;
                completionService = value ?? new SqlCompletionService();
                completionService.MetadataChanged += CompletionService_MetadataChanged;
                semanticColorizer.CompletionService = completionService;
                Editor.TextArea.TextView.Redraw();
            }
        }

        /// <summary>
        /// 获取编辑器全部 SQL 文本。
        /// </summary>
        public string Text
        {
            get { return Editor.Text; }
            set { Editor.Text = value ?? string.Empty; }
        }

        /// <summary>
        /// 获取选中 SQL；没有选区时返回全部 SQL。
        /// </summary>
        public string SelectedOrAllText
        {
            get
            {
                return string.IsNullOrWhiteSpace(Editor.SelectedText) ? Editor.Text : Editor.SelectedText;
            }
        }

        /// <summary>
        /// 聚焦到 SQL 编辑器。
        /// </summary>
        public void FocusEditor()
        {
            Editor.Focus();
        }

        /// <summary>
        /// 在当前光标位置插入 SQL 文本，如果已经选中文本则先替换选区。
        /// </summary>
        public void InsertTextAtCaret(string text)
        {
            string insertText = text ?? string.Empty;
            if (string.IsNullOrEmpty(insertText))
            {
                return;
            }

            if (Editor.SelectionLength > 0)
            {
                int selectionStart = Editor.SelectionStart;
                Editor.Document.Replace(selectionStart, Editor.SelectionLength, insertText);
                Editor.CaretOffset = selectionStart + insertText.Length;
            }
            else
            {
                int insertOffset = Math.Max(0, Math.Min(Editor.CaretOffset, Editor.Document.TextLength));
                Editor.Document.Insert(insertOffset, insertText);
                Editor.CaretOffset = insertOffset + insertText.Length;
            }

            CloseCompletionWindow();
            FocusEditor();
        }

        /// <summary>
        /// 关闭当前自动提示窗口，避免切换数据库或格式化 SQL 后继续显示旧候选。
        /// </summary>
        public void CloseCompletionWindow()
        {
            if (completionWindow != null)
            {
                completionWindow.Close();
                completionWindow = null;
            }
        }

        /// <summary>
        /// 重新绘制语义着色，数据库或架构作用域变化后调用。
        /// </summary>
        public void RefreshSemanticColoring()
        {
            Editor.TextArea.TextView.Redraw();
        }

        /// <summary>
        /// 处理 Ctrl+Space 主动触发提示。
        /// </summary>
        private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ShowCompletion();
                e.Handled = true;
                return;
            }

            if (completionWindow != null && (e.Key == Key.Tab || e.Key == Key.Enter || e.Key == Key.Return))
            {
                completionWindow.CompletionList.RequestInsertion(e);
                e.Handled = true;
                return;
            }
        }

        /// <summary>
        /// 输入点号或空格时尝试弹出上下文提示。
        /// </summary>
        private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (e.Text == "." || e.Text == " ")
            {
                ShowCompletion();
                return;
            }

            if (e.Text.Length == 1 && char.IsLetter(e.Text[0]))
            {
                ShowCompletion();
            }
        }

        /// <summary>
        /// 在用户输入非单词字符时提交当前选中的提示项。
        /// </summary>
        private void TextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (completionWindow == null || string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            char input = e.Text[0];
            if (!char.IsLetterOrDigit(input) && input != '_')
            {
                completionWindow.Close();
            }
        }

        /// <summary>
        /// 元数据缓存变化后刷新编辑器语义着色。
        /// </summary>
        private void CompletionService_MetadataChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(delegate { Editor.TextArea.TextView.Redraw(); }));
                return;
            }

            Editor.TextArea.TextView.Redraw();
        }

        /// <summary>
        /// 根据当前 SQL 上下文显示自动提示窗口。
        /// </summary>
        private void ShowCompletion()
        {
            int startOffset;
            int endOffset;
            string prefix;
            GetCompletionSegment(out startOffset, out endOffset, out prefix);

            var items = completionService.GetItems(Editor.Text, Editor.CaretOffset);
            if (items.Count == 0)
            {
                if (completionWindow != null)
                {
                    completionWindow.Close();
                }

                return;
            }

            if (completionWindow == null)
            {
                completionWindow = new CompletionWindow(Editor.TextArea);
                completionWindow.Width = 280;
                completionWindow.Height = 240;
                completionWindow.CloseWhenCaretAtBeginning = true;
                completionWindow.CompletionList.IsFiltering = false;
                completionWindow.Closed += delegate { completionWindow = null; };
                completionWindow.Show();
            }

            RefreshCompletionList(items, startOffset, endOffset, prefix);
        }

        /// <summary>
        /// 刷新当前补全窗口的候选项，而不是反复关闭和重建窗口。
        /// </summary>
        private void RefreshCompletionList(System.Collections.Generic.IList<TXDB.Models.SqlCompletionItem> items, int startOffset, int endOffset, string prefix)
        {
            if (completionWindow == null)
            {
                return;
            }

            completionWindow.StartOffset = startOffset;
            completionWindow.EndOffset = endOffset;
            completionWindow.CompletionList.CompletionData.Clear();

            foreach (SqlCompletionData item in items.Select(item => new SqlCompletionData(item)))
            {
                completionWindow.CompletionList.CompletionData.Add(item);
            }

            completionWindow.CompletionList.SelectItem(prefix);
            if (completionWindow.CompletionList.SelectedItem == null && completionWindow.CompletionList.CompletionData.Count > 0)
            {
                completionWindow.CompletionList.ListBox.SelectIndex(0);
            }
        }

        /// <summary>
        /// 计算自动提示插入时需要替换的当前输入前缀区间。
        /// </summary>
        private void GetCompletionSegment(out int startOffset, out int endOffset, out string prefix)
        {
            string text = Editor.Text ?? string.Empty;
            endOffset = Math.Max(0, Math.Min(Editor.CaretOffset, text.Length));
            startOffset = endOffset;

            while (startOffset > 0 && IsIdentifierChar(text[startOffset - 1]))
            {
                startOffset--;
            }

            prefix = text.Substring(startOffset, endOffset - startOffset);
        }

        /// <summary>
        /// 判断字符是否属于普通 SQL 标识符，用于计算补全替换范围。
        /// </summary>
        private static bool IsIdentifierChar(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_' || value == '$';
        }
    }
}
