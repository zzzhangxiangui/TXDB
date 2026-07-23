namespace TXDB.Models
{
    /// <summary>
    /// 表预览快速筛选运算符的显示项。
    /// </summary>
    public class TableFilterOperatorOption
    {
        /// <summary>
        /// 运算符内部编码。
        /// </summary>
        public string OperatorCode { get; set; }

        /// <summary>
        /// 运算符界面显示文本。
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 初始化筛选运算符显示项。
        /// </summary>
        public TableFilterOperatorOption()
        {
            OperatorCode = string.Empty;
            DisplayName = string.Empty;
        }

        /// <summary>
        /// TXComboBox 选中区域会直接展示对象本身，这里返回显示文本避免出现类型名。
        /// </summary>
        public override string ToString()
        {
            return DisplayName ?? string.Empty;
        }
    }
}
