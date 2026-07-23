using System.ComponentModel;

namespace TXDB.Services
{
    /// <summary>
    /// 为界面绑定对象提供属性变更通知。
    /// </summary>
    public abstract class NotifyObject : INotifyPropertyChanged
    {
        /// <summary>
        /// 属性值变化事件。
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 触发属性值变化事件。
        /// </summary>
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
