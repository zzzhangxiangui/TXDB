namespace TXDB.Models
{
    /// <summary>
    /// 表示一个可以快速插入到 SQL 编辑器的语句模板。
    /// </summary>
    public class SqlTemplateInfo
    {
        /// <summary>
        /// 模板标题。
        /// </summary>
        public string Title { get; private set; }

        /// <summary>
        /// 模板 SQL 文本。
        /// </summary>
        public string Sql { get; private set; }

        /// <summary>
        /// 初始化 SQL 模板。
        /// </summary>
        public SqlTemplateInfo(string title, string sql)
        {
            Title = title ?? string.Empty;
            Sql = sql ?? string.Empty;
        }
    }
}
