using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TXDB.Models;

namespace TXDB.Services
{
    /// <summary>
    /// 负责连接配置的本地读取、保存和密码加密。
    /// </summary>
    public class ConnectionProfileStore
    {
        private readonly string filePath;
        private readonly string legacyFilePath;

        /// <summary>
        /// 初始化连接配置存储服务。
        /// </summary>
        public ConnectionProfileStore()
        {
            filePath = AppStoragePaths.ConnectionsFile;
            legacyFilePath = AppStoragePaths.LegacyConnectionsFile;
        }

        /// <summary>
        /// 从本地配置文件加载连接配置。
        /// </summary>
        public List<ConnectionProfile> Load()
        {
            if (File.Exists(filePath))
            {
                List<ConnectionProfile> profiles = JsonConvert.DeserializeObject<List<ConnectionProfile>>(File.ReadAllText(filePath, Encoding.UTF8)) ?? new List<ConnectionProfile>();
                DecryptProfiles(profiles);
                return profiles;
            }

            if (File.Exists(legacyFilePath))
            {
                List<ConnectionProfile> profiles = LoadLegacyProfiles();
                Save(profiles);
                return profiles;
            }

            return new List<ConnectionProfile>();
        }

        /// <summary>
        /// 保存连接配置到本地配置文件。
        /// </summary>
        public void Save(IEnumerable<ConnectionProfile> profiles)
        {
            List<ConnectionProfile> saveProfiles = BuildSaveProfiles(profiles, false);

            AppStoragePaths.EnsureFolder(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonConvert.SerializeObject(saveProfiles, Formatting.Indented), Encoding.UTF8);
        }

        /// <summary>
        /// 从指定 JSON 文件导入连接配置，支持单个连接对象或连接数组。
        /// </summary>
        public IList<ConnectionProfile> ImportFromFile(string fileName)
        {
            List<ConnectionProfile> importProfiles = ReadProfileFile(fileName);
            DecryptProfiles(importProfiles);
            return importProfiles;
        }

        /// <summary>
        /// 将指定连接配置导出到 JSON 文件，密码仍然按当前 Windows 用户进行 DPAPI 加密。
        /// </summary>
        public void ExportToFile(string fileName, IEnumerable<ConnectionProfile> profiles)
        {
            List<ConnectionProfile> saveProfiles = BuildSaveProfiles(profiles, true);
            AppStoragePaths.EnsureFolder(Path.GetDirectoryName(fileName));
            File.WriteAllText(fileName, JsonConvert.SerializeObject(saveProfiles, Formatting.Indented), Encoding.UTF8);
        }

        /// <summary>
        /// 读取旧版 XML 连接配置，并迁移到新的 JSON 文件。
        /// </summary>
        private List<ConnectionProfile> LoadLegacyProfiles()
        {
            using (FileStream stream = File.OpenRead(legacyFilePath))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(List<ConnectionProfile>));
                List<ConnectionProfile> profiles = serializer.Deserialize(stream) as List<ConnectionProfile>;
                profiles = profiles ?? new List<ConnectionProfile>();
                DecryptProfiles(profiles);
                return profiles;
            }
        }

        /// <summary>
        /// 生成可以写入磁盘的连接配置副本，避免把运行期明文密码序列化出去。
        /// </summary>
        private static List<ConnectionProfile> BuildSaveProfiles(IEnumerable<ConnectionProfile> profiles, bool keepEncryptedWhenPasswordEmpty)
        {
            List<ConnectionProfile> saveProfiles = new List<ConnectionProfile>();
            if (profiles == null)
            {
                return saveProfiles;
            }

            foreach (ConnectionProfile profile in profiles)
            {
                if (profile == null || profile.IsDesignSample)
                {
                    continue;
                }

                ConnectionProfile copy = profile.Clone();
                if (!copy.SavePassword)
                {
                    copy.EncryptedPassword = string.Empty;
                }
                else if (!string.IsNullOrEmpty(copy.Password))
                {
                    copy.EncryptedPassword = EncryptPassword(copy.Password);
                }
                else if (!keepEncryptedWhenPasswordEmpty)
                {
                    copy.EncryptedPassword = string.Empty;
                }

                copy.Password = string.Empty;
                saveProfiles.Add(copy);
            }

            return saveProfiles;
        }

        /// <summary>
        /// 读取连接 JSON 文件，兼容单个连接对象和数组两种结构。
        /// </summary>
        private static List<ConnectionProfile> ReadProfileFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
            {
                return new List<ConnectionProfile>();
            }

            string json = File.ReadAllText(fileName, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<ConnectionProfile>();
            }

            JToken token = JToken.Parse(json);
            if (token.Type == JTokenType.Array)
            {
                return token.ToObject<List<ConnectionProfile>>() ?? new List<ConnectionProfile>();
            }

            ConnectionProfile profile = token.ToObject<ConnectionProfile>();
            return profile == null ? new List<ConnectionProfile>() : new List<ConnectionProfile> { profile };
        }

        /// <summary>
        /// 解密连接配置中的密码，运行期只把明文放在 Password 字段。
        /// </summary>
        private static void DecryptProfiles(IEnumerable<ConnectionProfile> profiles)
        {
            if (profiles == null)
            {
                return;
            }

            foreach (ConnectionProfile profile in profiles)
            {
                if (profile != null)
                {
                    profile.Password = DecryptPassword(profile.EncryptedPassword);
                }
            }
        }

        /// <summary>
        /// 使用当前 Windows 用户上下文加密密码。
        /// </summary>
        private static string EncryptPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return string.Empty;
            }

            byte[] sourceBytes = Encoding.UTF8.GetBytes(password);
            byte[] protectedBytes = ProtectedData.Protect(sourceBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        /// <summary>
        /// 使用当前 Windows 用户上下文解密密码。
        /// </summary>
        private static string DecryptPassword(string encryptedPassword)
        {
            if (string.IsNullOrEmpty(encryptedPassword))
            {
                return string.Empty;
            }

            try
            {
                byte[] protectedBytes = Convert.FromBase64String(encryptedPassword);
                byte[] sourceBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(sourceBytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
