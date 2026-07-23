using System;
using TXDB.Models;

namespace TXDB.Views
{
    /// <summary>
    /// 查询页请求主窗口保存 SQL 时携带的数据。
    /// </summary>
    public class QuerySaveRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// 当前查询页使用的连接配置。
        /// </summary>
        public ConnectionProfile Profile { get; private set; }

        /// <summary>
        /// 当前查询页选择的数据库名称。
        /// </summary>
        public string DatabaseName { get; private set; }

        /// <summary>
        /// 当前查询页选择的架构名称。
        /// </summary>
        public string SchemaName { get; private set; }

        /// <summary>
        /// 需要保存的 SQL 文本。
        /// </summary>
        public string Sql { get; private set; }

        /// <summary>
        /// 初始化保存查询请求参数。
        /// </summary>
        public QuerySaveRequestedEventArgs(ConnectionProfile profile, string databaseName, string schemaName, string sql)
        {
            Profile = profile;
            DatabaseName = databaseName ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Sql = sql ?? string.Empty;
        }
    }
}
