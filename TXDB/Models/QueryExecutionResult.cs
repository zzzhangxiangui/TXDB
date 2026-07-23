using System;
using System.Data;

namespace TXDB.Models
{
    /// <summary>
    /// 表示 SQL 执行后的统一返回结果。
    /// </summary>
    public class QueryExecutionResult
    {
        /// <summary>
        /// 查询返回的数据表。
        /// </summary>
        public DataTable Table { get; set; }

        /// <summary>
        /// 执行消息。
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 受影响行数。
        /// </summary>
        public int AffectedRows { get; set; }

        /// <summary>
        /// 执行耗时。
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// 当前结果对应的数据总行数，普通 SQL 查询为 -1，表预览分页会写入真实总数。
        /// </summary>
        public int TotalRows { get; set; }

        /// <summary>
        /// 初始化查询结果。
        /// </summary>
        public QueryExecutionResult()
        {
            Table = new DataTable();
            Message = string.Empty;
            AffectedRows = -1;
            Duration = TimeSpan.Zero;
            TotalRows = -1;
        }
    }
}
