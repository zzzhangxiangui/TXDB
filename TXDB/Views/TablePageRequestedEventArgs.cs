using System;
using System.Collections.Generic;
using TXDB.Models;

namespace TXDB.Views
{
    /// <summary>
    /// 表预览页请求加载某一页数据时使用的事件参数。
    /// </summary>
    public class TablePageRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// 当前预览页使用的连接配置。
        /// </summary>
        public ConnectionProfile Profile { get; private set; }

        /// <summary>
        /// 当前预览的表或视图节点。
        /// </summary>
        public DatabaseObjectNode TableNode { get; private set; }

        /// <summary>
        /// 从 1 开始的页码。
        /// </summary>
        public int PageIndex { get; private set; }

        /// <summary>
        /// 每页显示行数。
        /// </summary>
        public int PageSize { get; private set; }

        /// <summary>
        /// 当前页请求携带的快速筛选条件。
        /// </summary>
        public IList<TableFilterCondition> Filters { get; private set; }

        /// <summary>
        /// 初始化表预览分页请求参数。
        /// </summary>
        public TablePageRequestedEventArgs(ConnectionProfile profile, DatabaseObjectNode tableNode, int pageIndex, int pageSize)
            : this(profile, tableNode, pageIndex, pageSize, null)
        {
        }

        /// <summary>
        /// 初始化带快速筛选条件的表预览分页请求参数。
        /// </summary>
        public TablePageRequestedEventArgs(ConnectionProfile profile, DatabaseObjectNode tableNode, int pageIndex, int pageSize, IList<TableFilterCondition> filters)
        {
            Profile = profile;
            TableNode = tableNode;
            PageIndex = pageIndex;
            PageSize = pageSize;
            Filters = filters ?? new List<TableFilterCondition>();
        }
    }
}
