using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using TXDB.Services;

namespace TXDB.Views
{
    /// <summary>
    /// SQL 编辑器语义着色器，根据关键字、表名和字段名缓存给文本设置不同颜色。
    /// </summary>
    public class SqlSemanticColorizer : DocumentColorizingTransformer
    {
        // SQL 标识符匹配规则，首版覆盖常见英文表名、字段名和下划线命名。
        private static readonly Regex WordRegex = new Regex(@"[A-Za-z_][A-Za-z0-9_$]*", RegexOptions.Compiled);

        // 编辑器语义色：关键字蓝色、表名绿色、字段名紫色。
        private static readonly Brush KeywordBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        private static readonly Brush TableBrush = new SolidColorBrush(Color.FromRgb(13, 148, 136));
        private static readonly Brush ColumnBrush = new SolidColorBrush(Color.FromRgb(124, 58, 237));
        private static readonly Brush StringBrush = new SolidColorBrush(Color.FromRgb(180, 83, 9));
        private static readonly Brush CommentBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));

        // 着色器从自动提示服务中读取实时元数据缓存。
        private SqlCompletionService completionService;
        private ISet<string> keywordTexts;
        private ISet<string> tableTexts;
        private ISet<string> columnTexts;

        /// <summary>
        /// 初始化 SQL 语义着色器。
        /// </summary>
        public SqlSemanticColorizer(SqlCompletionService completionService)
        {
            SetCompletionService(completionService ?? new SqlCompletionService());
        }

        /// <summary>
        /// 当前用于读取关键字、表名和字段名缓存的自动提示服务。
        /// </summary>
        public SqlCompletionService CompletionService
        {
            get { return completionService; }
            set { SetCompletionService(value ?? new SqlCompletionService()); }
        }

        /// <summary>
        /// 对当前可见行应用语义着色。
        /// </summary>
        protected override void ColorizeLine(DocumentLine line)
        {
            if (completionService == null || CurrentContext == null || CurrentContext.Document == null)
            {
                return;
            }

            string lineText = CurrentContext.Document.GetText(line);
            ISet<string> keywords = keywordTexts ?? completionService.GetKeywordTexts();
            ISet<string> tables = tableTexts ?? completionService.GetTableTexts();
            ISet<string> columns = columnTexts ?? completionService.GetColumnTexts();
            List<ColorRange> protectedRanges = BuildStringAndCommentRanges(lineText);

            foreach (ColorRange range in protectedRanges)
            {
                ApplyBrush(line.Offset + range.Start, line.Offset + range.End, range.Brush, false);
            }

            foreach (Match match in WordRegex.Matches(lineText))
            {
                if (IsInsideProtectedRange(protectedRanges, match.Index))
                {
                    continue;
                }

                string word = match.Value;
                Brush brush = null;
                bool isBold = false;

                if (keywords.Contains(word))
                {
                    brush = KeywordBrush;
                    isBold = true;
                }
                else if (tables.Contains(word))
                {
                    brush = TableBrush;
                }
                else if (columns.Contains(word))
                {
                    brush = ColumnBrush;
                }

                if (brush != null)
                {
                    int startOffset = line.Offset + match.Index;
                    int endOffset = startOffset + match.Length;
                    ApplyBrush(startOffset, endOffset, brush, isBold);
                }
            }
        }

        /// <summary>
        /// 统一切换词库来源，并在元数据变更时保持缓存同步。
        /// </summary>
        /// <param name="service">新的补全服务。</param>
        private void SetCompletionService(SqlCompletionService service)
        {
            if (completionService != null)
            {
                completionService.MetadataChanged -= CompletionService_MetadataChanged;
            }

            completionService = service ?? new SqlCompletionService();
            completionService.MetadataChanged += CompletionService_MetadataChanged;
            RefreshCachedTexts();
        }

        /// <summary>
        /// 元数据变化后刷新缓存词库。
        /// </summary>
        private void CompletionService_MetadataChanged(object sender, EventArgs e)
        {
            RefreshCachedTexts();
        }

        /// <summary>
        /// 重新读取当前补全服务中的关键字、表名和字段名缓存。
        /// </summary>
        private void RefreshCachedTexts()
        {
            if (completionService == null)
            {
                keywordTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                tableTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                columnTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            keywordTexts = completionService.GetKeywordTexts();
            tableTexts = completionService.GetTableTexts();
            columnTexts = completionService.GetColumnTexts();
        }

        /// <summary>
        /// 构造当前行中字符串和注释的着色区间。
        /// </summary>
        private static List<ColorRange> BuildStringAndCommentRanges(string lineText)
        {
            List<ColorRange> ranges = new List<ColorRange>();
            bool insideString = false;
            int stringStart = -1;

            for (int position = 0; position < lineText.Length; position++)
            {
                if (!insideString && position + 1 < lineText.Length && lineText[position] == '-' && lineText[position + 1] == '-')
                {
                    ranges.Add(new ColorRange(position, lineText.Length, CommentBrush));
                    break;
                }

                if (lineText[position] == '\'')
                {
                    if (insideString && position + 1 < lineText.Length && lineText[position + 1] == '\'')
                    {
                        position++;
                        continue;
                    }

                    insideString = !insideString;
                    if (insideString)
                    {
                        stringStart = position;
                    }
                    else if (stringStart >= 0)
                    {
                        ranges.Add(new ColorRange(stringStart, position + 1, StringBrush));
                        stringStart = -1;
                    }
                }
            }

            if (insideString && stringStart >= 0)
            {
                ranges.Add(new ColorRange(stringStart, lineText.Length, StringBrush));
            }

            return ranges;
        }

        /// <summary>
        /// 判断单词位置是否落在字符串或注释区间内。
        /// </summary>
        private static bool IsInsideProtectedRange(IEnumerable<ColorRange> ranges, int index)
        {
            foreach (ColorRange range in ranges)
            {
                if (index >= range.Start && index < range.End)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 给指定文本片段应用颜色和可选加粗样式。
        /// </summary>
        private void ApplyBrush(int startOffset, int endOffset, Brush brush, bool isBold)
        {
            ChangeLinePart(startOffset, endOffset, delegate(VisualLineElement element)
            {
                element.TextRunProperties.SetForegroundBrush(brush);
                if (isBold)
                {
                    Typeface currentTypeface = element.TextRunProperties.Typeface;
                    element.TextRunProperties.SetTypeface(new Typeface(
                        currentTypeface.FontFamily,
                        FontStyles.Normal,
                        FontWeights.SemiBold,
                        FontStretches.Normal));
                }
            });
        }

        /// <summary>
        /// 表示一段需要独立着色的文本区间。
        /// </summary>
        private class ColorRange
        {
            /// <summary>
            /// 区间起始位置，基于当前行文本。
            /// </summary>
            public int Start { get; private set; }

            /// <summary>
            /// 区间结束位置，基于当前行文本。
            /// </summary>
            public int End { get; private set; }

            /// <summary>
            /// 当前区间使用的画刷。
            /// </summary>
            public Brush Brush { get; private set; }

            /// <summary>
            /// 初始化着色区间。
            /// </summary>
            public ColorRange(int start, int end, Brush brush)
            {
                Start = start;
                End = end;
                Brush = brush;
            }
        }
    }
}
