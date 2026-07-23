namespace TXDB.Models
{
    /// <summary>
    /// 表示 SQL 自动提示项的来源类型。
    /// </summary>
    public enum SqlCompletionItemKind
    {
        /// <summary>
        /// SQL 关键字。
        /// </summary>
        Keyword,

        /// <summary>
        /// 数据库名称。
        /// </summary>
        Database,

        /// <summary>
        /// 架构名称。
        /// </summary>
        Schema,

        /// <summary>
        /// 表或视图名称。
        /// </summary>
        Table,

        /// <summary>
        /// 字段名称。
        /// </summary>
        Column,

        /// <summary>
        /// 常用函数。
        /// </summary>
        Function
    }

    /// <summary>
    /// 表示一个可以插入到 SQL 编辑器中的自动提示项。
    /// </summary>
    public class SqlCompletionItem
    {
        /// <summary>
        /// 插入文本。
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// 提示说明。
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 提示项类型。
        /// </summary>
        public SqlCompletionItemKind Kind { get; set; }

        /// <summary>
        /// 字段所属表名。
        /// </summary>
        public string OwnerName { get; set; }

        /// <summary>
        /// 提示项所属数据库名称，用于按查询页选择的数据库隔离候选项。
        /// </summary>
        public string DatabaseName { get; set; }

        /// <summary>
        /// 提示项所属架构名称，用于按查询页选择的架构隔离候选项。
        /// </summary>
        public string SchemaName { get; set; }

        /// <summary>
        /// 排序优先级。
        /// </summary>
        public double Priority { get; set; }
    }
}
