using System;
using System.Threading;
using TXDB.Models;

namespace TXDB.Views
{
    /// <summary>
    /// 查询页请求主窗口执行 SQL 时携带的数据。
    /// </summary>
    public class QueryExecuteRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// 当前查询页使用的连接配置。
        /// </summary>
        public ConnectionProfile Profile { get; private set; }

        /// <summary>
        /// 需要执行的 SQL 文本。
        /// </summary>
        public string Sql { get; private set; }

        /// <summary>
        /// 当前执行请求的取消标记，停止按钮会通过它通知主窗口忽略后续结果。
        /// </summary>
        public CancellationToken CancellationToken { get; private set; }

        /// <summary>
        /// 初始化执行请求参数。
        /// </summary>
        public QueryExecuteRequestedEventArgs(ConnectionProfile profile, string sql, CancellationToken cancellationToken)
        {
            Profile = profile;
            Sql = sql;
            CancellationToken = cancellationToken;
        }
    }
}
