using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreSystem.Api.Data;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace StoreSystem.Api
{
    /// <summary>
    /// Web serverni (Kestrel + API) talab bo'yicha ishga tushiradi/to'xtatadi.
    /// Port band bo'lsa oldindan tekshirish mumkin — shu sabab "port band" deb
    /// kutilmaganda yiqilib tushmaydi.
    /// </summary>
    public class ServerHost
    {
        private WebApplication? _app;

        public bool IsRunning => _app != null;
        public int RunningPort { get; private set; }

        /// <summary>Port bo'shmi (boshqa dastur band qilmaganmi)?</summary>
        public static bool IsPortFree(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>preferred dan boshlab birinchi bo'sh portni topadi (topilmasa -1).</summary>
        public static int FindFreePort(int preferred)
        {
            for (int p = preferred; p <= preferred + 20 && p <= 65535; p++)
                if (IsPortFree(p)) return p;
            return -1;
        }

        /// <summary>Serverni berilgan portda ishga tushiradi. Muvaffaqiyatsiz bo'lsa exception otadi.</summary>
        public void Start(int port)
        {
            if (_app != null) return;

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory
            });

            // Barcha tarmoq interfeyslarida tinglaymiz (LAN/WiFi dagi qurilmalar uchun).
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

            // Zaxiradan tiklashda baza fayli yuklanadi — fayl hajmi cheklovini olib tashlaymiz
            // (do'kon bazasi odatda kichik, ammo katta bazalar uchun ham xavfsiz bo'lsin).
            builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = null);
            builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
            {
                o.MultipartBodyLengthLimit = long.MaxValue;
                o.ValueLengthLimit = int.MaxValue;
            });

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler =
                        System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                });
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={ServerConfig.DbPath}"));
            builder.Services.AddCors(options =>
                options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            var app = builder.Build();

            // SQLite: jadvallarni model bo'yicha yaratadi (+ admin seed). Migration kerak emas.
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
                EnsureSchemaUpgrades(db); // eski bazalarga yangi ustunlarni qo'shadi
            }

            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseCors("AllowAll");
            app.MapControllers();

            // MUHIM: StartAsync'ni Task.Run ichida bajaramiz. Aks holda WinForms
            // UI thread'ining SynchronizationContext'i continuation'larni bloklab
            // qo'yadi va keyinchalik to'xtatishda deadlock yuzaga keladi.
            Task.Run(() => app.StartAsync()).GetAwaiter().GetResult();

            _app = app;
            RunningPort = port;
        }

        public void Stop()
        {
            if (_app == null) return;

            // Avval havolani uzamiz — holat darhol "to'xtatilgan" bo'ladi va
            // takroriy bosishlar qayta to'xtatishga urinmaydi.
            var app = _app;
            _app = null;
            RunningPort = 0;

            try
            {
                // Task.Run — UI thread'ining sync-context'ini chetlab o'tamiz.
                // Shu sabab StopAsync continuation'lari bloklangan UI thread'ni
                // kutib qotib qolmaydi (ilgari shu yerda osilib qolardi).
                Task.Run(async () =>
                {
                    await app.StopAsync(TimeSpan.FromSeconds(5));
                    await app.DisposeAsync();
                }).GetAwaiter().GetResult();
            }
            catch { /* to'xtatishdagi xatolarni e'tiborsiz qoldiramiz */ }
        }

        // ─────────────────────────────────────────────────────────────
        //  SXEMA YANGILANISHI
        //  EnsureCreated mavjud jadvalni O'ZGARTIRMAYDI. Yangi versiyada
        //  qo'shilgan ustunlar eski bazalarda ham bo'lishi uchun — yo'q
        //  bo'lsa, ularni xavfsiz qo'shamiz (ma'lumotlar saqlanib qoladi).
        // ─────────────────────────────────────────────────────────────
        private static void EnsureSchemaUpgrades(AppDbContext db)
        {
            AddColumnIfMissing(db, "Orders", "CashAmount", "REAL NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "Orders", "CardAmount", "REAL NOT NULL DEFAULT 0");
            // Vozvrat (qaytarish) — shu chekdan jami qaytarilgan summa.
            AddColumnIfMissing(db, "Orders", "ReturnedSum", "REAL NOT NULL DEFAULT 0");

            // Chegirma (skidka) — buyurtma qatorining chegirmagacha bo'lgan asl narxi.
            AddColumnIfMissing(db, "OrderItems", "OriginalPrice", "REAL NOT NULL DEFAULT 0");

            // ── VOZVRAT (QAYTARISH) jadvallari ──────────────────────
            // EnsureCreated mavjud bazaga yangi jadval qo'shmaydi — shu sabab
            // eski o'rnatilgan serverlarda ham qo'lda yaratamiz.
            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""Returns"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""OrderId"" INTEGER NOT NULL DEFAULT 0,
                ""ClientId"" INTEGER NULL,
                ""CashierId"" INTEGER NULL,
                ""TotalSum"" REAL NOT NULL DEFAULT 0,
                ""CashRefund"" REAL NOT NULL DEFAULT 0,
                ""CardRefund"" REAL NOT NULL DEFAULT 0,
                ""DebtReduced"" REAL NOT NULL DEFAULT 0,
                ""RefundType"" TEXT NOT NULL DEFAULT 'Cash',
                ""Reason"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL DEFAULT '');");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""ReturnItems"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""ReturnId"" INTEGER NOT NULL DEFAULT 0,
                ""OrderItemId"" INTEGER NOT NULL DEFAULT 0,
                ""ProductId"" INTEGER NOT NULL DEFAULT 0,
                ""Quantity"" INTEGER NOT NULL DEFAULT 0,
                ""Price"" REAL NOT NULL DEFAULT 0);");

            // ── OLUV (KIRIM) MODULI jadvallari ──────────────────────
            // EnsureCreated MAVJUD bazaga yangi jadval QO'SHMAYDI. Shu sabab eski
            // bazalarda bu jadvallar bo'lmasligi mumkin — yo'q bo'lsa yaratamiz.
            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""Suppliers"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name"" TEXT NOT NULL DEFAULT '',
                ""Phone"" TEXT NULL,
                ""Note"" TEXT NULL,
                ""DebtBalance"" REAL NOT NULL DEFAULT 0,
                ""DebtDueDate"" TEXT NULL,
                ""DebtReminderDone"" INTEGER NOT NULL DEFAULT 0,
                ""CreatedAt"" TEXT NOT NULL DEFAULT '');");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""Purchases"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""SupplierId"" INTEGER NOT NULL DEFAULT 0,
                ""TotalSum"" REAL NOT NULL DEFAULT 0,
                ""PaidSum"" REAL NOT NULL DEFAULT 0,
                ""Status"" TEXT NOT NULL DEFAULT 'Paid',
                ""DueDate"" TEXT NULL,
                ""Note"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL DEFAULT '');");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""PurchaseItems"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""PurchaseId"" INTEGER NOT NULL DEFAULT 0,
                ""ProductId"" INTEGER NOT NULL DEFAULT 0,
                ""Quantity"" INTEGER NOT NULL DEFAULT 0,
                ""PiecesPerBlock"" INTEGER NOT NULL DEFAULT 1,
                ""UnitCost"" REAL NOT NULL DEFAULT 0);");

            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""SupplierPayments"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""SupplierId"" INTEGER NOT NULL DEFAULT 0,
                ""Amount"" REAL NOT NULL DEFAULT 0,
                ""RemainingAfter"" REAL NOT NULL DEFAULT 0,
                ""PaidAt"" TEXT NOT NULL DEFAULT '',
                ""Note"" TEXT NULL);");

            // ── SINXRON jadvali (offline -> server operatsiyalari) ──────
            // EnsureCreated mavjud bazaga yangi jadval qo'shmaydi — shu sabab
            // eski o'rnatilgan serverlarda ham qo'lda yaratamiz.
            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""SyncOperations"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""OperationId"" TEXT NOT NULL DEFAULT '',
                ""Type"" TEXT NOT NULL DEFAULT '',
                ""EntityId"" INTEGER NOT NULL DEFAULT 0,
                ""Amount"" REAL NOT NULL DEFAULT 0,
                ""AppliedAmount"" REAL NOT NULL DEFAULT 0,
                ""Note"" TEXT NULL,
                ""ClientCreatedAt"" TEXT NOT NULL DEFAULT '',
                ""AppliedAt"" TEXT NOT NULL DEFAULT '',
                ""Device"" TEXT NULL);");

            // OperationId bo'yicha yagona indeks (takror qo'llanmaslik kafolati)
            ExecRaw(db, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SyncOperations_OperationId""
                ON ""SyncOperations"" (""OperationId"");");

            // Yangi versiyada qo'shilgan ustun: kirim eslatmasi ko'rib chiqilganmi.
            AddColumnIfMissing(db, "Purchases", "ReminderDone", "INTEGER NOT NULL DEFAULT 0");

            // Firma boshlang'ich (ilovagacha bo'lgan) qarz eslatmasi ustunlari.
            AddColumnIfMissing(db, "Suppliers", "DebtDueDate", "TEXT NULL");
            AddColumnIfMissing(db, "Suppliers", "DebtReminderDone", "INTEGER NOT NULL DEFAULT 0");

            // Mijoz boshlang'ich (ilovagacha bo'lgan) qarz eslatmasi ustunlari.
            // EnsureCreated mavjud Clients jadvalini o'zgartirmaydi — shu sabab
            // eski bazalarda bu ustunlar yo'q bo'lsa, xavfsiz qo'shamiz.
            AddColumnIfMissing(db, "Clients", "DebtDueDate", "TEXT NULL");
            AddColumnIfMissing(db, "Clients", "DebtReminderDone", "INTEGER NOT NULL DEFAULT 0");

            // ── SOZLAMALAR jadvali (kalit/qiymat) ──────────────────────
            // EnsureCreated mavjud bazaga yangi jadval qo'shmaydi — shu sabab
            // eski o'rnatilgan serverlarda ham qo'lda yaratamiz (Settings).
            ExecRaw(db, @"CREATE TABLE IF NOT EXISTS ""Settings"" (
                ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Key"" TEXT NOT NULL DEFAULT '',
                ""Value"" TEXT NOT NULL DEFAULT '');");
            ExecRaw(db, @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Settings_Key""
                ON ""Settings"" (""Key"");");
        }

        // Bitta SQL buyrug'ini bajaradi (jadval yaratish kabi).
        private static void ExecRaw(AppDbContext db, string sql)
        {
            var conn = db.Database.GetDbConnection();
            bool wasClosed = conn.State != System.Data.ConnectionState.Open;
            if (wasClosed) conn.Open();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            finally
            {
                if (wasClosed) conn.Close();
            }
        }

        private static void AddColumnIfMissing(AppDbContext db, string table, string column, string definition)
        {
            var conn = db.Database.GetDbConnection();
            bool wasClosed = conn.State != System.Data.ConnectionState.Open;
            if (wasClosed) conn.Open();
            try
            {
                bool exists = false;
                using (var check = conn.CreateCommand())
                {
                    check.CommandText = $"PRAGMA table_info(\"{table}\");";
                    using var r = check.ExecuteReader();
                    while (r.Read())
                    {
                        // PRAGMA table_info ustunlari: 0=cid, 1=name, 2=type, ...
                        if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        { exists = true; break; }
                    }
                }
                if (!exists)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
                    alter.ExecuteNonQuery();
                }
            }
            finally
            {
                if (wasClosed) conn.Close();
            }
        }
    }
}