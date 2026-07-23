using System;
using System.Xml.Serialization;
using Newtonsoft.Json;

namespace TXDB.Models
{
    /// <summary>
    /// 表示一个本地保存的数据库连接配置。
    /// </summary>
    public class ConnectionProfile
    {
        /// <summary>
        /// 连接配置唯一标识。
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 连接显示名称。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 数据库类型。
        /// </summary>
        public DatabaseProviderType ProviderType { get; set; }

        /// <summary>
        /// 服务器地址。
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// 服务器端口。
        /// </summary>
        public string Port { get; set; }

        /// <summary>
        /// 数据库名称、服务名或初始目录。
        /// </summary>
        public string DatabaseName { get; set; }

        /// <summary>
        /// 登录用户名。
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// 文件型数据库路径。
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// 用户手动输入的完整连接字符串。
        /// </summary>
        public string RawConnectionString { get; set; }

        /// <summary>
        /// 是否使用 Windows 集成身份验证。
        /// </summary>
        public bool UseWindowsAuthentication { get; set; }

        /// <summary>
        /// 是否保存密码。
        /// </summary>
        public bool SavePassword { get; set; }

        /// <summary>
        /// 使用 DPAPI 加密后的密码。
        /// </summary>
        public string EncryptedPassword { get; set; }

        /// <summary>
        /// 运行期明文密码，不直接写入配置文件。
        /// </summary>
        [XmlIgnore]
        [JsonIgnore]
        public string Password { get; set; }

        /// <summary>
        /// 设计期示例连接标识，不写入配置文件。
        /// </summary>
        [XmlIgnore]
        [JsonIgnore]
        public bool IsDesignSample { get; set; }

        /// <summary>
        /// 初始化默认连接配置。
        /// </summary>
        public ConnectionProfile()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = string.Empty;
            Host = string.Empty;
            Port = string.Empty;
            DatabaseName = string.Empty;
            UserName = string.Empty;
            FilePath = string.Empty;
            RawConnectionString = string.Empty;
            Password = string.Empty;
            EncryptedPassword = string.Empty;
        }

        /// <summary>
        /// 克隆当前连接配置，避免编辑弹窗直接修改原对象。
        /// </summary>
        public ConnectionProfile Clone()
        {
            return new ConnectionProfile
            {
                Id = Id,
                Name = Name,
                ProviderType = ProviderType,
                Host = Host,
                Port = Port,
                DatabaseName = DatabaseName,
                UserName = UserName,
                FilePath = FilePath,
                RawConnectionString = RawConnectionString,
                UseWindowsAuthentication = UseWindowsAuthentication,
                SavePassword = SavePassword,
                EncryptedPassword = EncryptedPassword,
                Password = Password,
                IsDesignSample = IsDesignSample
            };
        }
    }
}
