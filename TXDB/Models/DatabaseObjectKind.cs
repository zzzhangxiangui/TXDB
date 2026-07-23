namespace TXDB.Models
{
    /// <summary>
    /// 表示数据库对象树中的节点类型。
    /// </summary>
    public enum DatabaseObjectKind
    {
        /// <summary>
        /// 数据库连接节点。
        /// </summary>
        Connection,

        /// <summary>
        /// 数据库节点。
        /// </summary>
        Database,

        /// <summary>
        /// 架构节点。
        /// </summary>
        Schema,

        /// <summary>
        /// 表节点。
        /// </summary>
        Table,

        /// <summary>
        /// 视图节点。
        /// </summary>
        View,

        /// <summary>
        /// 字段节点。
        /// </summary>
        Column,

        /// <summary>
        /// 索引节点。
        /// </summary>
        Index,

        /// <summary>
        /// 文件夹分组节点。
        /// </summary>
        Folder,

        /// <summary>
        /// 表文件夹节点。
        /// </summary>
        TablesFolder,

        /// <summary>
        /// 视图文件夹节点。
        /// </summary>
        ViewsFolder,

        /// <summary>
        /// 已保存查询文件夹节点。
        /// </summary>
        QueriesFolder,

        /// <summary>
        /// 已保存查询语句节点。
        /// </summary>
        SavedQuery,

        /// <summary>
        /// 信息提示节点。
        /// </summary>
        Info
    }
}
