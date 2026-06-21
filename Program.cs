using StoreSystem.Api;
using System;
using System.Linq;
using System.Windows.Forms;

// ═══════════════════════════════════════════════════════════════════
//  JESKO SERVER
//  ASP.NET Core Web API + Windows tray (soat yonidagi "J" ikonka).
//  Ishlash tartibi:
//    • Qo'lda ochilganda  -> boshqaruv oynasi chiqadi. Foydalanuvchi portni
//      tanlaydi, "Ishga tushirish" bosadi. Port band bo'lsa — tushunarli
//      xabar, server YIQILMAYDI.
//    • Kompyuter yonganda (--silent) -> server bo'sh portda jim ishga tushadi.
//  Ma'lumotlar bazasi: SQLite (bitta fayl). Tarmoq: http://0.0.0.0:<port>.
// ═══════════════════════════════════════════════════════════════════

internal static class Program
{
    private const string MutexName = @"Global\JESKO_STORE_SERVER_SINGLETON";

    [STAThread]
    private static void Main(string[] args)
    {
        // ── Bitta nusxa (single instance) ──
        bool isNew;
        System.Threading.Mutex? mutex = null;
        try
        {
            mutex = new System.Threading.Mutex(true, MutexName, out isNew);
        }
        catch
        {
            isNew = false; // mutex boshqa (admin) jarayonda — server allaqachon ishlayapti
        }

        if (!isNew)
        {
            // Server allaqachon ishlayapti — birinchi nusxaga "oynani ko'rsat" signalini
            // yuboramiz (fayl orqali), so'ng chiqamiz. Birinchi nusxa oynasini ochadi.
            try
            {
                ServerConfig.EnsureDir();
                System.IO.File.WriteAllText(ServerConfig.ShowFlagPath, "show");
            }
            catch { }
            return;
        }

        ServerConfig.EnsureDir();
        try { if (System.IO.File.Exists(ServerConfig.ShowFlagPath)) System.IO.File.Delete(ServerConfig.ShowFlagPath); }
        catch { }

        var config = ServerConfig.Load();

        bool silent = args.Any(a =>
            a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/silent", StringComparison.OrdinalIgnoreCase));

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var host = new ServerHost();

        // Kompyuter yonganda (Startup yorlig'i --silent bilan): serverni avtomatik,
        // BO'SH portda ishga tushiramiz — hech qanday oyna yoki xatosiz.
        if (silent)
        {
            try
            {
                int port = ServerHost.FindFreePort(config.Port);
                if (port < 0) port = config.Port;
                if (port != config.Port) { config.Port = port; config.Save(); }
                host.Start(port);
            }
            catch { /* silent rejimda xato ko'rsatmaymiz; oynadan qayta urinish mumkin */ }
        }

        using (var tray = new TrayAppContext(config, host, showWindowOnStart: !silent))
        {
            Application.Run(tray);
        }

        host.Stop();
        GC.KeepAlive(mutex); // mutex butun ishlash davomida ushlab turilsin
    }
}
