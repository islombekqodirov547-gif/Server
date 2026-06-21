using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminController(AppDbContext db) => _db = db;

    // ─────────────────────────────────────────────────────────────
    //  BARCHA MA'LUMOTLARNI TOZALASH (Factory reset)
    //  Xodimlar, mijozlar, mahsulotlar, kirimlar (Stocks), buyurtmalar
    //  va ularning tarixini butunlay o'chiradi. So'ng baza bo'sh
    //  qolmasligi (admin qulflanib qolmasligi) uchun yangi standart
    //  admin yaratiladi:  username = admin,  parol = 7707
    // ─────────────────────────────────────────────────────────────
    [HttpPost("reset")]
    public async Task<IActionResult> ResetAll([FromBody] ResetRequest req)
    {
        // Oddiy himoya: noto'g'ri kalit bilan tasodifan chaqirilmasligi uchun
        if (req?.ConfirmKey != "7707")
            return Unauthorized(new { message = "Tasdiqlash kaliti noto'g'ri." });

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // ── BARCHA jadvallarni FK bog'lanishlari tartibida o'chiramiz ──
            //  MUHIM: avval BOLA (child) jadvallar, keyin OTA (parent) jadvallar.
            //  Ilgari Suppliers/Purchases/PurchaseItems/SupplierPayments o'chirilmas
            //  edi — natijada PurchaseItems -> Products (Restrict) bog'lanishi sabab
            //  "DELETE FROM Products" YIQILIB, butun tranzaksiya orqaga qaytardi va
            //  HECH NARSA tozalanmasdi (mahsulot/firma/mijoz qolib ketardi).
            //  Endi hamma narsa to'g'ri tartibda, to'liq tozalanadi.
            //  Har bir o'chirish TryExecuteAsync orqali — eski bazada bir jadval
            //  bo'lmasa ham tozalash to'xtab qolmaydi.

            // 1) Eng "bola" jadvallar (boshqalarga bog'liq)
            await TryExecuteAsync("DELETE FROM ReturnItems");
            await TryExecuteAsync("DELETE FROM Returns");
            await TryExecuteAsync("DELETE FROM OrderItems");
            await TryExecuteAsync("DELETE FROM Orders");
            await TryExecuteAsync("DELETE FROM PurchaseItems");
            await TryExecuteAsync("DELETE FROM SupplierPayments");
            await TryExecuteAsync("DELETE FROM Purchases");
            await TryExecuteAsync("DELETE FROM Stocks");
            await TryExecuteAsync("DELETE FROM ProductBarcodes");
            await TryExecuteAsync("DELETE FROM DebtPayments");
            await TryExecuteAsync("DELETE FROM SyncOperations");

            // 2) "Ota" jadvallar (endi ularga bog'liq qatorlar qolmadi)
            await TryExecuteAsync("DELETE FROM Products");
            await TryExecuteAsync("DELETE FROM Clients");
            await TryExecuteAsync("DELETE FROM Suppliers");
            await TryExecuteAsync("DELETE FROM Users");

            // Identity (ID hisoblagich) larni 0 ga qaytaramiz — yangi yozuvlar 1 dan boshlanadi
            await ReseedAsync("ReturnItems");
            await ReseedAsync("Returns");
            await ReseedAsync("OrderItems");
            await ReseedAsync("Orders");
            await ReseedAsync("PurchaseItems");
            await ReseedAsync("SupplierPayments");
            await ReseedAsync("Purchases");
            await ReseedAsync("Stocks");
            await ReseedAsync("ProductBarcodes");
            await ReseedAsync("DebtPayments");
            await ReseedAsync("SyncOperations");
            await ReseedAsync("Products");
            await ReseedAsync("Clients");
            await ReseedAsync("Suppliers");
            await ReseedAsync("Users");

            // Standart admin (qulflanib qolmaslik uchun)
            _db.Users.Add(new User
            {
                FullName = "Administrator",
                Username = "admin",
                Password = "7707",
                Role = "Admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            await tx.CommitAsync();
            return Ok(new { message = "Barcha ma'lumotlar tozalandi. Standart admin: admin / 7707" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { message = "Tozalashda xatolik: " + ex.Message });
        }
    }

    // Reseed qilinishi mumkin bo'lgan jadvallarning QAT'IY ro'yxati.
    // Faqat shu nomlar qabul qilinadi — tashqaridan kelgan qiymat ishlatilmaydi,
    // shu sabab SQL injection imkoni yo'q.
    private static readonly string[] _reseedableTables =
        { "OrderItems", "Orders", "Stocks", "ProductBarcodes", "DebtPayments", "Products", "Clients", "Users",
          "ReturnItems", "Returns", "PurchaseItems", "SupplierPayments", "Purchases", "Suppliers", "SyncOperations" };

    private async Task ReseedAsync(string table)
    {
        // Ro'yxatda yo'q nom — umuman bajarilmaydi
        if (Array.IndexOf(_reseedableTables, table) < 0) return;
        try
        {
            // SQLite: AUTOINCREMENT hisoblagichi 'sqlite_sequence' jadvalida saqlanadi.
            // O'sha satrni o'chirsak — keyingi ID lar 1 dan boshlanadi.
            // 'table' yuqoridagi qat'iy ro'yxatdan keladi (foydalanuvchi kiritmaydi) — injection yo'q.
            var sql = "DELETE FROM sqlite_sequence WHERE name = '" + table + "'";
#pragma warning disable EF1002 // Jadval nomi ichki whitelist'dan — injection xavfi yo'q
            await _db.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
        }
        catch { /* sqlite_sequence yo'q bo'lsa — muhim emas */ }
    }

    // Jadval hali yaratilmagan bo'lishi mumkin (masalan ProductBarcodes) —
    // bunday holda DELETE xato bermay, e'tiborsiz qoldiriladi.
    private async Task TryExecuteAsync(string sql)
    {
        try { await _db.Database.ExecuteSqlRawAsync(sql); }
        catch { /* jadval yo'q — muhim emas */ }
    }
}

public class ResetRequest
{
    public string? ConfirmKey { get; set; }
}