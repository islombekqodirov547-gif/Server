using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

// Yetkazib beruvchilar (firmalar) — mahsulot olinadigan tashkilotlar.
// Mijozlar (Clients) bilan bir xil naqsh: CRUD + qarz to'lash + to'lov tarixi.
[ApiController]
[Route("api/[controller]")]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _db;
    public SuppliersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _db.Suppliers.OrderBy(s => s.Name).ToListAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var s = await _db.Suppliers.FindAsync(id);
        return s == null ? NotFound() : Ok(s);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Supplier supplier)
    {
        supplier.Id = 0;
        supplier.CreatedAt = DateTime.UtcNow;
        // Firma qarzi odatda 0 dan boshlanadi (qarz keyin kirim orqali qo'shiladi).
        // AMMO ilovagacha bo'lgan (eski) qarz bo'lsa — buxgalter "switch" orqali
        // boshlang'ich qarzni kiritishi mumkin. Manfiy qiymat 0 ga keltiriladi.
        if (supplier.DebtBalance < 0) supplier.DebtBalance = 0;
        supplier.DebtBalance = Math.Round(supplier.DebtBalance, 2);

        // Boshlang'ich qarz bo'lmasa — eslatma sanasi ham keraksiz.
        if (supplier.DebtBalance <= 0.5)
        {
            supplier.DebtDueDate = null;
            supplier.DebtReminderDone = false;
        }

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();
        return Ok(supplier);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] Supplier updated)
    {
        var s = await _db.Suppliers.FindAsync(id);
        if (s == null) return NotFound();
        s.Name = updated.Name;
        s.Phone = updated.Phone;
        s.Note = updated.Note;
        // Eslatma sanasi tahrirlashda ham yangilanadi (qarzning o'zi to'lov/kirim
        // orqali boshqariladi — bu yerda DebtBalance'ga tegmaymiz).
        s.DebtDueDate = updated.DebtDueDate;
        // Yangi muddat qo'yilsa — eslatma qayta faollashsin.
        if (updated.DebtDueDate != null) s.DebtReminderDone = false;
        await _db.SaveChangesAsync();
        return Ok(s);
    }

    // ─────────────────────────────────────────────────────────────
    //  BOSHLANG'ICH QARZ ESLATMASI
    //  To'lash (eslatish) sanasi bugun yoki o'tib ketgan, qarzi hali bor
    //  va ko'rib chiqilmagan firmalar. Admin/Buxgalter paneli ochilganda
    //  banner shu firmalardan ham xabar beradi (kirim eslatmalari bilan birga).
    // ─────────────────────────────────────────────────────────────
    [HttpGet("due")]
    public async Task<IActionResult> Due()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        var list = await _db.Suppliers
            .Where(s => s.DebtDueDate != null
                        && s.DebtDueDate < tomorrow
                        && !s.DebtReminderDone
                        && s.DebtBalance > 0.5)
            .OrderBy(s => s.DebtDueDate)
            .ToListAsync();
        return Ok(list);
    }

    // Boshlang'ich qarz eslatmasini "ko'rib chiqildi" deb belgilash.
    [HttpPost("{id}/ack")]
    public async Task<IActionResult> AckReminder(int id)
    {
        var s = await _db.Suppliers.FindAsync(id);
        if (s == null) return NotFound();
        s.DebtReminderDone = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await _db.Suppliers.FindAsync(id);
        if (s == null) return NotFound();

        // Kirimlari bo'lsa o'chirib bo'lmaydi (tarix buzilmasligi uchun)
        bool hasPurchases = await _db.Purchases.AnyAsync(p => p.SupplierId == id);
        if (hasPurchases)
            return BadRequest(new { message = "Bu firmada kirimlar tarixi bor — o'chirib bo'lmaydi." });

        _db.Suppliers.Remove(s);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ─────────────────────────────────────────────────────────────
    //  FIRMAGA QARZ TO'LASH (nasiyani kamaytirish)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/payments")]
    public async Task<IActionResult> PayDebt(int id, [FromBody] SupplierPaymentRequest req)
    {
        var supplier = await _db.Suppliers.FindAsync(id);
        if (supplier == null) return NotFound();

        double amount = req?.Amount ?? 0;
        if (amount <= 0)
            return BadRequest(new { message = "To'lov summasi 0 dan katta bo'lishi kerak." });
        if (supplier.DebtBalance <= 0.5)
            return BadRequest(new { message = "Bu firmaga qarzimiz yo'q." });

        double applied = Math.Min(amount, supplier.DebtBalance);
        supplier.DebtBalance = Math.Round(supplier.DebtBalance - applied, 2);
        if (supplier.DebtBalance < 1) supplier.DebtBalance = 0;
        // Qarz to'liq yopilsa — boshlang'ich qarz eslatmasi ham keraksiz bo'ladi.
        if (supplier.DebtBalance <= 0.5) supplier.DebtReminderDone = true;

        var payment = new SupplierPayment
        {
            SupplierId = id,
            Amount = applied,
            RemainingAfter = supplier.DebtBalance,
            PaidAt = DateTime.UtcNow,
            Note = string.IsNullOrWhiteSpace(req?.Note) ? null : req!.Note!.Trim()
        };
        _db.SupplierPayments.Add(payment);
        await _db.SaveChangesAsync();

        return Ok(new { payment, supplier });
    }

    // Firmaga qilingan to'lovlar tarixi (eng yangisi birinchi)
    [HttpGet("{id}/payments")]
    public async Task<IActionResult> GetPayments(int id)
    {
        var list = await _db.SupplierPayments
            .Where(p => p.SupplierId == id)
            .OrderByDescending(p => p.PaidAt)
            .ToListAsync();
        return Ok(list);
    }

    // Firmadan kelgan kirimlar tarixi (eng yangisi birinchi)
    [HttpGet("{id}/purchases")]
    public async Task<IActionResult> GetPurchases(int id)
    {
        var list = await _db.Purchases
            .Where(p => p.SupplierId == id)
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        return Ok(list);
    }
}

public class SupplierPaymentRequest
{
    public double Amount { get; set; }
    public string? Note { get; set; }
}
