using System.Linq;
using System.Windows;
using TX.WpfUI.Theming;
using TXDB.Services;

namespace TXDB
{
    /// <summary>
    /// 应用程序入口，负责初始化 TX.WpfUI 全局主题。
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 应用启动时写入控件库默认主题，保证 TX 控件和原生控件都能拿到主题颜色。
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // 控件库主题资源已经在 App.xaml 合并，这里优先应用用户上次选择的主题。
            if (TXThemeManager.Themes.Count > 0)
            {
                TXThemeInfo savedTheme = FindSavedTheme();
                TXThemeManager.ApplyTheme(savedTheme ?? TXThemeManager.Themes[0]);
            }
        }

        /// <summary>
        /// 从用户设置中查找上次选择的主题。
        /// </summary>
        private static TXThemeInfo FindSavedTheme()
        {
            string themeName = new AppSettingsStore().Load().ThemeName;
            if (string.IsNullOrWhiteSpace(themeName))
            {
                return null;
            }

            return TXThemeManager.Themes.FirstOrDefault(item => item.Name == themeName);
        }
    }
}
