using TXDB.Services;

namespace TXDB.Models
{
    /// <summary>
    /// 表预览页的单条快速筛选条件。
    /// </summary>
    public class TableFilterCondition : NotifyObject
    {
        private bool isEnabled;
        private string logic;
        private string columnName;
        private string operatorCode;
        private string value;

        /// <summary>
        /// 是否启用当前筛选条件。
        /// </summary>
        public bool IsEnabled
        {
            get { return isEnabled; }
            set
            {
                if (isEnabled == value)
                {
                    return;
                }

                isEnabled = value;
                OnPropertyChanged("IsEnabled");
            }
        }

        /// <summary>
        /// 与上一条条件之间的逻辑关系，首条条件会忽略该值。
        /// </summary>
        public string Logic
        {
            get { return logic; }
            set
            {
                string safeValue = string.IsNullOrWhiteSpace(value) ? "AND" : value;
                if (logic == safeValue)
                {
                    return;
                }

                logic = safeValue;
                OnPropertyChanged("Logic");
            }
        }

        /// <summary>
        /// 当前条件筛选的字段名称。
        /// </summary>
        public string ColumnName
        {
            get { return columnName; }
            set
            {
                string safeValue = value ?? string.Empty;
                if (columnName == safeValue)
                {
                    return;
                }

                columnName = safeValue;
                OnPropertyChanged("ColumnName");
            }
        }

        /// <summary>
        /// 当前条件使用的运算符编码。
        /// </summary>
        public string OperatorCode
        {
            get { return operatorCode; }
            set
            {
                string safeValue = string.IsNullOrWhiteSpace(value) ? "Contains" : value;
                if (operatorCode == safeValue)
                {
                    return;
                }

                operatorCode = safeValue;
                OnPropertyChanged("OperatorCode");
            }
        }

        /// <summary>
        /// 用户输入的筛选值，空值类运算符会忽略该值。
        /// </summary>
        public string Value
        {
            get { return value; }
            set
            {
                string safeValue = value ?? string.Empty;
                if (this.value == safeValue)
                {
                    return;
                }

                this.value = safeValue;
                OnPropertyChanged("Value");
            }
        }

        /// <summary>
        /// 初始化默认筛选条件。
        /// </summary>
        public TableFilterCondition()
        {
            isEnabled = true;
            logic = "AND";
            columnName = string.Empty;
            operatorCode = "Contains";
            value = string.Empty;
        }

        /// <summary>
        /// 复制一份独立条件，避免后台查询过程中被界面继续修改。
        /// </summary>
        public TableFilterCondition Clone()
        {
            return new TableFilterCondition
            {
                IsEnabled = IsEnabled,
                Logic = Logic,
                ColumnName = ColumnName,
                OperatorCode = OperatorCode,
                Value = Value
            };
        }
    }
}
