using System.Configuration;

namespace DlcvCSharpCppTest.Properties
{
    // 使用 .NET 用户级设置保存选择记录，与 DlcvDemo 的 LastModelPath 生命周期相同。
    internal sealed class Settings : ApplicationSettingsBase
    {
        private static readonly Settings defaultInstance = (Settings)Synchronized(new Settings());
        public static Settings Default { get { return defaultInstance; } }

        public Settings() { }

        // 非交互验证使用独立存储，不读取或修改实际用户配置。
        internal Settings(SettingsProvider provider)
        {
            foreach (SettingsProperty property in Properties)
                property.Provider = provider;
            Providers.Clear();
            Providers.Add(provider);
        }

        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string LastModelPath
        {
            get { return (string)this[nameof(LastModelPath)]; }
            set { this[nameof(LastModelPath)] = value; }
        }
    }
}
