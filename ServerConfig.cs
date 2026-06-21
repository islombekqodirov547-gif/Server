using System;
using System.IO;
using System.Text.Json;

namespace StoreSystem.Api
{
    /// <summary>
    /// Server sozlamalari (port) va fayl yo'llari.
    /// Sozlama va baza fayllari "Program Files" emas, balki YOZISH MUMKIN bo'lgan
    /// ProgramData papkasida saqlanadi:
    ///   C:\ProgramData\StoreSystem\server-config.json
    ///   C:\ProgramData\StoreSystem\store.db
    /// Shu sabab dastur Program Files ga o'rnatilsa ham bazaga muammosiz yozadi.
    /// </summary>
    public class ServerConfig
    {
        public int Port { get; set; } = 5050;

        public static string DataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "StoreSystem");

        public static string ConfigPath => Path.Combine(DataDir, "server-config.json");
        public static string DbPath => Path.Combine(DataDir, "store.db");
        // Ikkinchi nusxa ishga tushganda birinchi nusxaga "oynani ko'rsat" signali (fayl orqali)
        public static string ShowFlagPath => Path.Combine(DataDir, "show-window.flag");

        public static void EnsureDir() => Directory.CreateDirectory(DataDir);

        public static ServerConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(ConfigPath));
                    if (cfg != null && cfg.Port > 0 && cfg.Port <= 65535) return cfg;
                }
            }
            catch { /* buzilgan config — standartga qaytamiz */ }
            return new ServerConfig();
        }

        public void Save()
        {
            EnsureDir();
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
