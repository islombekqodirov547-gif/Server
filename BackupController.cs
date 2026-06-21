﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using System.Text;

namespace StoreSystem.Api.Controllers;

// ═══════════════════════════════════════════════════════════════════
//  ZAXIRA (BACKUP) / TIKLASH (RESTORE)
//  ───────────────────────────────────────────────────────────────────
//  Bu kontroller butun ma'lumotlar bazasini (mahsulotlar, mijozlar,
//  xodimlar, buyurtmalar, qarzlar, kirimlar — HAMMASINI) bitta faylga
//  saqlash va o'sha fayldan to'liq tiklash imkonini beradi.
//
//  Baza SQLite (bitta fayl) bo'lgani uchun "zaxira" = o'sha bazaning
//  to'liq, izchil (consistent) nusxasi. Bu eng ishonchli usul: hech
//  qanday ma'lumot yo'qolmaydi, ID lar va bog'lanishlar aynan saqlanadi.
//
//  • DOWNLOAD: VACUUM INTO bilan jonli bazaning toza nusxasi olinadi
//    (server ishlab turganda ham xavfsiz) va .jeskobackup fayli sifatida
//    yuklab beriladi.
//  • RESTORE: yuklangan fayl avval TEKSHIRILADI (haqiqiy SQLite va
//    kerakli jadvallar bormi). So'ng tiklashdan OLDIN joriy baza
//    avtomatik zaxiralanadi (xavfsizlik uchun). Tiklashning o'zi bitta
//    TRANZAKSIYA ichida bajariladi — ya'ni yo to'liq tiklanadi, yo
//    umuman o'zgarmaydi. Shu sabab baza HECH QACHON buzilib qolmaydi.
// ═══════════════════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
public class BackupController : ControllerBase
{
    private readonly AppDbContext _db;
    public BackupController(AppDbContext db) => _db = db;

    // Tiklash/zaxira ishlaydigan jadvallarning QAT'IY ro'yxati.
    // INSERT tartibi: avval "ota" jadvallar, keyin "bola" jadvallar (FK uchun xavfsiz).
    private static readonly string[] _tablesParentFirst =
        { "Users", "Clients", "Suppliers", "Products", "ProductBarcodes", "DebtPayments",
          "SupplierPayments", "Stocks", "Orders", "OrderItems", "Purchases", "PurchaseItems",
          "SyncOperations" };

    // SQLite fayl boshidagi "sehrli" imzo: "SQLite format 3\0"
    private static readonly byte[] _sqliteMagic = Encoding.ASCII.GetBytes("SQLite format 3\0");

    // ─────────────────────────────────────────────────────────────
    //  1) JORIY BAZA HAQIDA QISQA MA'LUMOT (dialogda ko'rsatish uchun)
    // ─────────────────────────────────────────────────────────────
    [HttpGet("info")]
    public async Task<IActionResult> Info()
    {
        try
        {
            var info = new
            {
                products = await _db.Products.CountAsync(),
                clients = await _db.Clients.CountAsync(),
                users = await _db.Users.CountAsync(),
                orders = await _db.Orders.CountAsync(),
                serverTime = DateTime.Now
            };
            return Ok(info);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Ma'lumot olishda xatolik: " + ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  2) ZAXIRA YUKLAB OLISH (export)
    //     Butun bazaning toza, izchil nusxasini .jeskobackup fayli
    //     sifatida qaytaradi.
    // ─────────────────────────────────────────────────────────────
    [HttpGet("download")]
    public async Task<IActionResult> Download()
    {
        ServerConfig.EnsureDir();
        var tempPath = Path.Combine(ServerConfig.DataDir, $"export-{Guid.NewGuid():N}.db");

        try
        {
            // VACUUM INTO — jonli bazaning toza, izchil (consistent) nusxasini yaratadi.
            // Bu server ishlab turganda ham xavfsiz va bo'sh joylarni siqib, fayl hajmini kichraytiradi.
            var escaped = tempPath.Replace("'", "''");
            await _db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{escaped}'");

            var bytes = await System.IO.File.ReadAllBytesAsync(tempPath);

            var fileName = $"JESKO-baza-{DateTime.Now:yyyy-MM-dd-HHmm}.jeskobackup";
            return File(bytes, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Zaxira yaratishda xatolik: " + ex.Message });
        }
        finally
        {
            try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); } catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  3) ZAXIRADAN TIKLASH (restore)
    //     Yuklangan faylni tekshiradi, joriy bazani avtomatik zaxiralaydi,
    //     so'ng bitta tranzaksiya ichida to'liq tiklaydi.
    // ─────────────────────────────────────────────────────────────
    [HttpPost("restore")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Restore(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Fayl tanlanmadi yoki bo'sh." });

        ServerConfig.EnsureDir();
        var incomingPath = Path.Combine(ServerConfig.DataDir, $"restore-{Guid.NewGuid():N}.db");

        try
        {
            // 3.1 — Yuklangan faylni vaqtinchalik diskka yozamiz
            await using (var fs = System.IO.File.Create(incomingPath))
                await file.CopyToAsync(fs);

            // 3.2 — TEKSHIRUV: bu haqiqiy SQLite faylimi va kerakli jadvallar bormi?
            var (valid, reason, userCount) = ValidateBackup(incomingPath);
            if (!valid)
                return BadRequest(new { message = "Bu fayl yaroqli JESKO zaxira fayli emas. " + reason });

            // 3.3 — XAVFSIZLIK: tiklashdan OLDIN joriy bazani avtomatik zaxiralaymiz.
            //        Agar nimadir noto'g'ri ketsa, eski holat yo'qolmaydi.
            TrySafetyCopy();

            // 3.4 — TIKLASH: bitta tranzaksiya ichida. Yo to'liq, yo umuman o'zgarmaydi.
            await RestoreFromAttachedAsync(incomingPath);

            // 3.5 — Yangi holatni qisqacha qaytaramiz
            var summary = new
            {
                products = await _db.Products.CountAsync(),
                clients = await _db.Clients.CountAsync(),
                users = await _db.Users.CountAsync(),
                orders = await _db.Orders.CountAsync()
            };

            return Ok(new
            {
                message = "Baza muvaffaqiyatli tiklandi.",
                restoredUsers = userCount,
                summary
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Tiklashda xatolik: " + ex.Message });
        }
        finally
        {
            try { if (System.IO.File.Exists(incomingPath)) System.IO.File.Delete(incomingPath); } catch { }
        }
    }

    // ── Zaxira faylini tekshirish ────────────────────────────────
    // Haqiqiy SQLite faylimi, "Users" jadvali bormi va kamida 1 ta foydalanuvchi bormi?
    private static (bool ok, string reason, int userCount) ValidateBackup(string path)
    {
        try
        {
            // Fayl imzosini tekshiramiz (SQLite format 3)
            using (var fs = System.IO.File.OpenRead(path))
            {
                var head = new byte[16];
                int read = fs.Read(head, 0, 16);
                if (read < 16 || !head.AsSpan(0, 16).SequenceEqual(_sqliteMagic))
                    return (false, "Fayl tuzilishi noto'g'ri.", 0);
            }

            // Faqat o'qish (read-only) rejimida ochib, jadvallarni tekshiramiz
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            using var conn = new SqliteConnection(cs);
            conn.Open();

            if (!TableExists(conn, "Users"))
                return (false, "Ichida 'Users' (xodimlar) jadvali topilmadi.", 0);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            if (count < 1)
                return (false, "Zaxirada birorta ham xodim yo'q (bo'sh baza).", 0);

            return (true, "", count);
        }
        catch (Exception ex)
        {
            return (false, "Faylni o'qib bo'lmadi: " + ex.Message, 0);
        }
    }

    // ── Joriy bazaning xavfsizlik nusxasini olish (tiklashdan oldin) ──
    private static void TrySafetyCopy()
    {
        try
        {
            if (!System.IO.File.Exists(ServerConfig.DbPath)) return;
            var backupsDir = Path.Combine(ServerConfig.DataDir, "backups");
            Directory.CreateDirectory(backupsDir);
            var dest = Path.Combine(backupsDir, $"avto-zaxira-{DateTime.Now:yyyy-MM-dd-HHmmss}.db");
            System.IO.File.Copy(ServerConfig.DbPath, dest, overwrite: true);
        }
        catch { /* xavfsizlik nusxasi muvaffaqiyatsiz bo'lsa ham tiklashni to'xtatmaymiz */ }
    }

    // ── Asosiy tiklash mantig'i ──────────────────────────────────
    // Jonli bazaga zaxira faylini ATTACH qilib, har bir jadvalni
    // tozalab, zaxiradan ko'chiramiz. Hammasi bitta tranzaksiyada.
    private async Task RestoreFromAttachedAsync(string incomingPath)
    {
        var conn = (SqliteConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var escaped = incomingPath.Replace("'", "''");

        // FK tekshiruvini vaqtincha o'chiramiz (tranzaksiyadan TASHQARIDA bo'lishi shart)
        await ExecAsync(conn, "PRAGMA foreign_keys = OFF;");
        await ExecAsync(conn, $"ATTACH DATABASE '{escaped}' AS src;");

        try
        {
            await using var tx = await conn.BeginTransactionAsync();

            // 1) Bola jadvallardan boshlab joriy bazani tozalaymiz (teskari tartib)
            foreach (var table in _tablesParentFirst.Reverse())
                await ExecInTxAsync(conn, tx, $"DELETE FROM main.\"{table}\";");

            // 2) Ota jadvallardan boshlab zaxiradan ko'chiramiz (to'g'ri tartib).
            //    USTUN-MOS (column-aware): faqat ikkala bazada ham mavjud ustunlarni
            //    ko'chiramiz. Shu sabab ESKI zaxirani YANGI sxemaga (yangi ustunlar
            //    qo'shilgan) muammosiz tiklash mumkin — yo'q ustunlar standart (0)
            //    qiymat oladi, ortiqcha ustunlar e'tiborsiz qoldiriladi.
            foreach (var table in _tablesParentFirst)
            {
                if (!TableExists(conn, table, "src")) continue; // eski zaxirada bu jadval bo'lmasligi mumkin
                var mainCols = GetColumns(conn, "main", table);
                var srcCols = GetColumns(conn, "src", table);
                var shared = mainCols.Where(c => srcCols.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
                if (shared.Count == 0) continue;
                string colList = string.Join(", ", shared.Select(c => $"\"{c}\""));
                await ExecInTxAsync(conn, tx,
                    $"INSERT INTO main.\"{table}\" ({colList}) SELECT {colList} FROM src.\"{table}\";");
            }

            // 3) Avtoinkrement hisoblagichlarini ham sinxronlaymiz (mavjud bo'lsa)
            if (TableExists(conn, "sqlite_sequence", "main") && TableExists(conn, "sqlite_sequence", "src"))
            {
                await ExecInTxAsync(conn, tx, "DELETE FROM main.sqlite_sequence;");
                await ExecInTxAsync(conn, tx, "INSERT INTO main.sqlite_sequence SELECT * FROM src.sqlite_sequence;");
            }

            await tx.CommitAsync();
        }
        finally
        {
            await ExecAsync(conn, "DETACH DATABASE src;");
            await ExecAsync(conn, "PRAGMA foreign_keys = ON;");
        }
    }

    // ── Yordamchi metodlar ───────────────────────────────────────
    private static List<string> GetColumns(SqliteConnection conn, string schema, string table)
    {
        var cols = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA {schema}.table_info(\"{table}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(1)); // PRAGMA table_info: 1 = ustun nomi
        return cols;
    }

    private static bool TableExists(SqliteConnection conn, string table, string schema = "main")
    {
        using var cmd = conn.CreateCommand();
        // sqlite_sequence ham sqlite_master da bo'lmaydi — alohida tekshiramiz
        if (string.Equals(table, "sqlite_sequence", StringComparison.OrdinalIgnoreCase))
            cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.sqlite_master WHERE name = 'sqlite_sequence';";
        else
            cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.sqlite_master WHERE type = 'table' AND name = $n;";
        cmd.Parameters.AddWithValue("$n", table);
        try { return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0; }
        catch { return false; }
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecInTxAsync(SqliteConnection conn, System.Data.Common.DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}