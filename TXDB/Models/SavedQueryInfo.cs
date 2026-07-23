using System;

namespace TXDB.Models
{
    /// <summary>
    /// 表示一个保存到本地的 SQL 查询语句。
    /// </summary>
    public class SavedQueryInfo
    {
        /// <summary>
        /// 保存查询的唯一标识。
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 所属连接配置标识，删除连接时按它一起清理。
        /// </summary>
        public string ConnectionId { get; set; }

        /// <summary>
        /// 所属连接显示名称，用于配置文件可读性和后续迁移参考。
        /// </summary>
        public string ConnectionName { get; set; }

        /// <summary>
        /// 保存查询时选择的数据库名称。
        /// </summary>
        public string DatabaseName { get; set; }

        /// <summary>
        /// 保存查询时选择的架构名称。
        /// </summary>
        public string SchemaName { get; set; }

        /// <summary>
        /// 查询标题，显示在左侧查询文件夹中。
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 实际保存的 SQL 文本。
        /// </summary>
        public string Sql { get; set; }

        /// <summary>
        /// 创建时间。
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 最后更新时间。
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 初始化保存查询对象。
        /// </summary>
        public SavedQueryInfo()
        {
            Id = Guid.NewGuid().ToString("N");
            ConnectionId = string.Empty;
            ConnectionName = string.Empty;
            DatabaseName = string.Empty;
            SchemaName = string.Empty;
            Title = string.Empty;
            Sql = string.Empty;
            CreatedAt = DateTime.Now;
            UpdatedAt = DateTime.Now;
        }
    }
}
