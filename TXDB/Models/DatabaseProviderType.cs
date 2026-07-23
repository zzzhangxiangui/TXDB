namespace TXDB.Models
{
    /// <summary>
    /// 表示首版工具需要支持的数据库类型。
    /// </summary>
    public enum DatabaseProviderType
    {
        /// <summary>
        /// SQL Server 数据库。
        /// </summary>
        SqlServer,

        /// <summary>
        /// SQLite 文件数据库。
        /// </summary>
        SQLite,

        /// <summary>
        /// Oracle 数据库。
        /// </summary>
        Oracle,

        /// <summary>
        /// Microsoft Access 文件数据库。
        /// </summary>
        Access,

        /// <summary>
        /// MySQL 数据库。
        /// </summary>
        MySQL
    }
}
