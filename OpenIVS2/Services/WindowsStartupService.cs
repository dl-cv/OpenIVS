using System;
using System.Reflection;
using Microsoft.Win32;

namespace OpenIVS2.Services
{
    public sealed class WindowsStartupService
    {
        private const string DefaultRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string DefaultValueName = "OpenIVS 2026";
        private const string LegacyValueName = "OpenIVS2";

        private readonly string _registryPath;
        private readonly string _valueName;
        private readonly string _executablePath;
        private readonly string _legacyValueName;

        public WindowsStartupService()
            : this(DefaultRegistryPath, DefaultValueName, ResolveExecutablePath(), LegacyValueName)
        {
        }

        internal WindowsStartupService(string registryPath, string valueName, string executablePath)
            : this(registryPath, valueName, executablePath, null)
        {
        }

        internal WindowsStartupService(string registryPath, string valueName, string executablePath, string legacyValueName)
        {
            if (string.IsNullOrWhiteSpace(registryPath)) throw new ArgumentException("注册表路径不能为空", "registryPath");
            if (string.IsNullOrWhiteSpace(valueName)) throw new ArgumentException("启动项名称不能为空", "valueName");
            if (string.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("程序路径不能为空", "executablePath");
            _registryPath = registryPath;
            _valueName = valueName;
            _executablePath = executablePath;
            _legacyValueName = legacyValueName;
        }

        public void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                using (var key = Registry.CurrentUser.CreateSubKey(_registryPath))
                {
                    if (key == null) throw new InvalidOperationException("无法打开当前用户启动项注册表");
                    key.SetValue(_valueName, BuildCommand(_executablePath), RegistryValueKind.String);
                    DeleteLegacyValue(key);
                }
                return;
            }

            using (var key = Registry.CurrentUser.OpenSubKey(_registryPath, true))
            {
                if (key != null)
                {
                    key.DeleteValue(_valueName, false);
                    DeleteLegacyValue(key);
                }
            }
        }

        public bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(_registryPath, false))
            {
                var value = key != null ? key.GetValue(_valueName) as string : null;
                return string.Equals(value, BuildCommand(_executablePath), StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static string BuildCommand(string executablePath)
        {
            return "\"" + executablePath + "\"";
        }

        private void DeleteLegacyValue(RegistryKey key)
        {
            if (!string.IsNullOrWhiteSpace(_legacyValueName) &&
                !string.Equals(_legacyValueName, _valueName, StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(_legacyValueName, false);
            }
        }

        private static string ResolveExecutablePath()
        {
            var assembly = Assembly.GetEntryAssembly();
            return assembly != null ? assembly.Location : null;
        }
    }
}
