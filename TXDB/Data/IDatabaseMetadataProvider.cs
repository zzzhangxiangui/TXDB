using System.Collections.Generic;
using System.Data;
using TXDB.Models;

namespace TXDB.Data
{
    /// <summary>
    /// 定义数据库对象元数据读取能力。
    /// </summary>
    public interface IDatabaseMetadataProvider
    {
        /// <summary>
        /// 读取数据库对象树元数据。
        /// </summary>
        IList<DatabaseObjectNode> LoadMetadata(ConnectionProfile profile);

        /// <summary>
        /// 读取指定数据库下的表、视图和字段元数据。
        /// </summary>
        DatabaseObjectNode LoadDatabaseMetadata(ConnectionProfile profile, string databaseName);

        /// <summary>
        /// 读取指定表或视图的字段结构。
        /// </summary>
        DataTable LoadTableSchema(ConnectionProfile profile, DatabaseObjectNode tableNode);
    }
}
