using System.Collections.ObjectModel;
using System.Windows.Media;
using TXDB.Services;

namespace TXDB.Models
{
    /// <summary>
    /// 表示左侧连接树实际绑定到界面的节点。
    /// </summary>
    public class DatabaseTreeNode : NotifyObject
    {
        // 节点展开状态会和 TreeViewItem 双向绑定，便于代码控制默认展开层级。
        private bool isExpanded;

        // 连接节点加载成功后置为 true，后续可用于筛选在线连接。
        private bool isConnected;

        // 节点图标画刷直接绑定到界面，用于连接状态和对象类型区分。
        private Brush iconBrush;

        // 节点文字画刷用于区分未打开、已打开、失败等状态。
        private Brush textBrush;
        private bool useCustomTextBrush;
        private string statusText;

        /// <summary>
        /// 节点显示名称。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 节点类型。
        /// </summary>
        public DatabaseObjectKind Kind { get; set; }

        /// <summary>
        /// 节点类型显示文本。
        /// </summary>
        public string DisplayType { get; set; }

        /// <summary>
        /// 节点图标颜色，连接节点会根据连接状态切换颜色。
        /// </summary>
        public Brush IconBrush
        {
            get { return iconBrush; }
            set
            {
                iconBrush = value;
                OnPropertyChanged("IconBrush");
            }
        }

        /// <summary>
        /// 节点文字颜色，未打开的连接、数据库和架构会使用更淡的颜色。
        /// </summary>
        public Brush TextBrush
        {
            get { return textBrush; }
            set
            {
                textBrush = value;
                OnPropertyChanged("TextBrush");
            }
        }

        /// <summary>
        /// 是否使用自定义文字颜色，普通表节点默认继续跟随主题文字颜色。
        /// </summary>
        public bool UseCustomTextBrush
        {
            get { return useCustomTextBrush; }
            set
            {
                useCustomTextBrush = value;
                OnPropertyChanged("UseCustomTextBrush");
            }
        }

        /// <summary>
        /// 当前连接是否已经成功加载元数据，仅连接节点使用。
        /// </summary>
        public bool IsConnected
        {
            get { return isConnected; }
            set
            {
                isConnected = value;
                OnPropertyChanged("IsConnected");
            }
        }

        /// <summary>
        /// 节点状态文字。
        /// </summary>
        public string StatusText
        {
            get { return statusText; }
            set
            {
                statusText = value;
                OnPropertyChanged("StatusText");
            }
        }

        /// <summary>
        /// 是否展开当前节点。
        /// </summary>
        public bool IsExpanded
        {
            get { return isExpanded; }
            set
            {
                isExpanded = value;
                OnPropertyChanged("IsExpanded");
            }
        }

        /// <summary>
        /// 连接配置，仅连接节点有值。
        /// </summary>
        public ConnectionProfile Profile { get; set; }

        /// <summary>
        /// 父级树节点，用于从表节点反查所属连接。
        /// </summary>
        public DatabaseTreeNode Parent { get; set; }

        /// <summary>
        /// 数据库对象元数据，仅对象节点有值。
        /// </summary>
        public DatabaseObjectNode Metadata { get; set; }

        /// <summary>
        /// 已保存查询节点关联的查询信息，仅查询节点使用。
        /// </summary>
        public SavedQueryInfo SavedQuery { get; set; }

        /// <summary>
        /// 下级节点集合。
        /// </summary>
        public ObservableCollection<DatabaseTreeNode> Children { get; private set; }

        /// <summary>
        /// 初始化树节点。
        /// </summary>
        public DatabaseTreeNode()
        {
            Name = string.Empty;
            DisplayType = string.Empty;
            StatusText = string.Empty;
            IconBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            TextBrush = null;
            UseCustomTextBrush = false;
            Children = new ObservableCollection<DatabaseTreeNode>();
        }
    }
}
