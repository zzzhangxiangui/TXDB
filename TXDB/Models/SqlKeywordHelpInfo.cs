namespace TXDB.Models
{
    /// <summary>
    /// 表示一个 SQL 关键字的中文说明、语法和示例。
    /// </summary>
    public class SqlKeywordHelpInfo
    {
        /// <summary>
        /// SQL 关键字。
        /// </summary>
        public string Keyword { get; private set; }

        /// <summary>
        /// 关键字中文含义。
        /// </summary>
        public string ChineseName { get; private set; }

        /// <summary>
        /// 关键字用途说明。
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        /// 常用语法。
        /// </summary>
        public string Syntax { get; private set; }

        /// <summary>
        /// 使用示例。
        /// </summary>
        public string Example { get; private set; }

        /// <summary>
        /// 注意事项。
        /// </summary>
        public string Notes { get; private set; }

        /// <summary>
        /// 左侧列表显示文本。
        /// </summary>
        public string ListText
        {
            get { return Keyword + "  " + ChineseName; }
        }

        /// <summary>
        /// 初始化 SQL 关键字帮助项。
        /// </summary>
        public SqlKeywordHelpInfo(string keyword, string chineseName, string description, string syntax, string example, string notes)
        {
            Keyword = keyword ?? string.Empty;
            ChineseName = chineseName ?? string.Empty;
            Description = description ?? string.Empty;
            Syntax = syntax ?? string.Empty;
            Example = example ?? string.Empty;
            Notes = notes ?? string.Empty;
        }
    }
}
