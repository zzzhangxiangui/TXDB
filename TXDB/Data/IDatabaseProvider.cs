using System.Collections.Generic;
using System.Data;
using TXDB.Models;

namespace TXDB.Data
{
    /// <summary>
    /// 定义不同数据库 Provider 必须实现的基础操作。
    /// </summary>
    public interface IDatabaseProvider
    {
        /// <summary>
        /// 当前 Provider 对应的数据库类型。
        /// </summary>
        DatabaseProviderType ProviderType { get; }

        /// <summary>
        /// 当前 Provider 的显示名称。
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// 根据连接配置创建数据库连接。
        /// </summary>
        IDbConnection CreateConnection(ConnectionProfile profile);

        /// <summary>
        /// 根据连接配置生成连接字符串。
        /// </summary>
        string BuildConnectionString(ConnectionProfile profile);

        /// <summary>
        /// 测试连接是否可用。
        /// </summary>
        void TestConnection(ConnectionProfile profile);

        /// <summary>
        /// 执行 SQL 并返回统一结果。
        /// </summary>
        QueryExecutionResult Execute(ConnectionProfile profile, string sql);

        /// <summary>
        /// 读取指定表或视图的分页预览数据，仅用于双击表打开的数据页。
        /// </summary>
        QueryExecutionResult PreviewTable(ConnectionProfile profile, DatabaseObjectNode tableNode, int pageIndex, int pageSize);

        /// <summary>
        /// 按快速筛选条件读取指定表或视图的分页预览数据。
        /// </summary>
        QueryExecutionResult PreviewTable(ConnectionProfile profile, DatabaseObjectNode tableNode, int pageIndex, int pageSize, IList<TableFilterCondition> filters);

        /// <summary>
        /// 保存表预览页中的新增、修改和删除行。
        /// </summary>
        int ApplyTableChanges(ConnectionProfile profile, DatabaseObjectNode tableNode, System.Data.DataTable changes);
    }
}
