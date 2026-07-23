using System;
using System.IO;

namespace TXDB.Services
{
    /// <summary>
    /// 统一管理 TXDB 本地持久化目录。
    /// </summary>
    public static class AppStoragePaths
    {
        /// <summary>
        /// 获取 TXDB 本地数据根目录，固定使用程序所在目录，方便整体复制和便携使用。
        /// </summary>
        public static string RootFolder
        {
            get
            {
                string baseFolder = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    baseFolder = Environment.CurrentDirectory;
                }

                return NormalizeFolderPath(baseFolder);
            }
        }

        /// <summary>
        /// 获取连接配置目录。
        /// </summary>
        public static string ConnectionsFolder
        {
            get { return Path.Combine(RootFolder, "connections"); }
        }

        /// <summary>
        /// 获取系统设置目录。
        /// </summary>
        public static string SettingsFolder
        {
            get { return Path.Combine(RootFolder, "settings"); }
        }

        /// <summary>
        /// 获取保存查询目录。
        /// </summary>
        public static string QueriesFolder
        {
            get { return Path.Combine(RootFolder, "queries"); }
        }

        /// <summary>
        /// 获取连接配置 JSON 文件路径。
        /// </summary>
        public static string ConnectionsFile
        {
            get { return Path.Combine(ConnectionsFolder, "connections.json"); }
        }

        /// <summary>
        /// 获取系统设置 JSON 文件路径。
        /// </summary>
        public static string SettingsFile
        {
            get { return Path.Combine(SettingsFolder, "settings.json"); }
        }

        /// <summary>
        /// 获取旧版连接 XML 文件路径，用于兼容迁移。
        /// </summary>
        public static string LegacyConnectionsFile
        {
            get { return Path.Combine(RootFolder, "connections.xml"); }
        }

        /// <summary>
        /// 获取旧版保存查询 XML 文件路径，用于兼容迁移。
        /// </summary>
        public static string LegacySavedQueriesFile
        {
            get { return Path.Combine(RootFolder, "saved-queries.xml"); }
        }

        /// <summary>
        /// 确保目录存在。
        /// </summary>
        public static void EnsureFolder(string folder)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        /// <summary>
        /// 将数据库或连接名称转换成适合作为文件名的文本。
        /// </summary>
        public static string ToSafeFileName(string value)
        {
            string safeName = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalidChar, '_');
            }

            return safeName;
        }

        /// <summary>
        /// 标准化目录路径，方便比较和拼接。
        /// </summary>
        private static string NormalizeFolderPath(string folder)
        {
            return Path.GetFullPath(folder ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
