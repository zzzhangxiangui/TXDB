using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using TXDB.Models;

namespace TXDB.Views
{
    /// <summary>
    /// AvalonEdit 自动提示窗口需要的提示项适配对象。
    /// </summary>
    public class SqlCompletionData : ICompletionData
    {
        private readonly SqlCompletionItem item;

        /// <summary>
        /// 初始化 SQL 自动提示项。
        /// </summary>
        public SqlCompletionData(SqlCompletionItem item)
        {
            this.item = item;
        }

        /// <summary>
        /// 插入文本。
        /// </summary>
        public string Text
        {
            get { return item.Text; }
        }

        /// <summary>
        /// 提示列表中显示的内容。
        /// </summary>
        public object Content
        {
            get { return item.Text; }
        }

        /// <summary>
        /// 提示项说明。
        /// </summary>
        public object Description
        {
            get { return item.Description; }
        }

        /// <summary>
        /// 提示项图标，首版先不显示图标。
        /// </summary>
        public ImageSource Image
        {
            get { return null; }
        }

        /// <summary>
        /// 提示项排序优先级。
        /// </summary>
        public double Priority
        {
            get { return item.Priority; }
        }

        /// <summary>
        /// 将当前提示项插入到编辑器中。
        /// </summary>
        public void Complete(TextArea textArea, ISegment completionSegment, System.EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}
