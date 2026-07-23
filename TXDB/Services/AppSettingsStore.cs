using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace TXDB.Services
{
    /// <summary>
    /// 负责读取和保存 TXDB 的系统级本地设置。
    /// </summary>
    public class AppSettingsStore
    {
        private readonly string filePath;

        /// <summary>
        /// 初始化系统设置存储服务。
        /// </summary>
        public AppSettingsStore()
        {
            filePath = AppStoragePaths.SettingsFile;
        }

        /// <summary>
        /// 读取系统设置，优先使用 JSON 文件，旧版用户设置只作为兼容兜底。
        /// </summary>
        public AppSettingsData Load()
        {
            if (File.Exists(filePath))
            {
                return JsonConvert.DeserializeObject<AppSettingsData>(File.ReadAllText(filePath, Encoding.UTF8)) ?? new AppSettingsData();
            }

            AppSettingsData settings = new AppSettingsData();
            settings.ThemeName = TXDB.Properties.Settings.Default.ThemeName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(settings.ThemeName))
            {
                Save(settings);
            }

            return settings;
        }

        /// <summary>
        /// 保存系统设置到 JSON 文件。
        /// </summary>
        public void Save(AppSettingsData settings)
        {
            AppStoragePaths.EnsureFolder(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonConvert.SerializeObject(settings ?? new AppSettingsData(), Formatting.Indented), Encoding.UTF8);
        }

        /// <summary>
        /// 只更新主题名称。
        /// </summary>
        public void SaveThemeName(string themeName)
        {
            AppSettingsData settings = Load();
            settings.ThemeName = themeName ?? string.Empty;
            Save(settings);
        }
    }

    /// <summary>
    /// 表示 TXDB 系统级设置文件内容。
    /// </summary>
    public class AppSettingsData
    {
        /// <summary>
        /// 用户最后一次选择的主题名称。
        /// </summary>
        public string ThemeName { get; set; }

        /// <summary>
        /// 初始化系统级设置默认值。
        /// </summary>
        public AppSettingsData()
        {
            ThemeName = string.Empty;
        }
    }
}
