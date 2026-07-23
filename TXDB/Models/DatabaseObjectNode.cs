using System.Collections.ObjectModel;

namespace TXDB.Models
{
    /// <summary>
    /// 表示从数据库元数据中读取出来的对象节点。
    /// </summary>
    public class DatabaseObjectNode
    {
        /// <summary>
        /// 对象名称。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 对象类型。
        /// </summary>
        public DatabaseObjectKind Kind { get; set; }

        /// <summary>
        /// 所属数据库名称。
        /// </summary>
        public string DatabaseName { get; set; }

        /// <summary>
        /// 所属架构名称。
        /// </summary>
        public string SchemaName { get; set; }

        /// <summary>
        /// 所属表或视图名称。
        /// </summary>
        public string ParentName { get; set; }

        /// <summary>
        /// 字段或对象的数据类型。
        /// </summary>
        public string DataType { get; set; }

        /// <summary>
        /// 字段或对象的注释说明，数据库驱动能读取到时填充。
        /// </summary>
        public string Comment { get; set; }

        /// <summary>
        /// 节点下级对象集合。
        /// </summary>
        public ObservableCollection<DatabaseObjectNode> Children { get; private set; }

        /// <summary>
        /// 初始化数据库对象节点。
        /// </summary>
        public DatabaseObjectNode()
        {
            Children = new ObservableCollection<DatabaseObjectNode>();
        }
    }
}
