using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ClientsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _db.Clients.OrderBy(c => c.Name).ToListAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var c = await _db.Clients.FindAsync(id);
        return c == null ? NotFound() : Ok(c);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Client client)
    {
        client.Id = 0;
        client.CreatedAt = DateTime.UtcNow;
        // Mijoz qarzi odatda 0 dan boshlanadi (qarz keyin nasiya savdo orqali qo'shiladi).
        // AMMO ilovagacha bo'lgan (eski) qarz bo'lsa — admin/buxgalter "switch" orqali
        // boshlang'ich qarzni kiritishi mumkin. Manfiy qiymat 0 ga keltiriladi.
        if (client.DebtBalance < 0) client.DebtBalance = 0;
        client.DebtBalance = Math.Round(client.DebtBalance, 2);

        // Boshlang'ich qarz bo'lmasa — eslatma sanasi ham keraksiz.
        if (client.DebtBalance <= 0.5)
        {
            client.DebtDueDate = null;
            client.DebtReminderDone = false;
        }

        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return Ok(client);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] Client updated)
    {
        var c = await _db.Clients.FindAsync(id);
        if (c == null) return NotFound();
        c.Name = updated.Name;
        c.Phone = updated.Phone;
        // Qarzning o'zi nasiya savdo / to'lov orqali boshqariladi — bu yerda
        // DebtBalance'ga tegmaymiz, faqat eslatish sanasini yangilaymiz.
        c.DebtDueDate = updated.DebtDueDate;
        // Yangi muddat qo'yilsa — eslatma qayta faollashsin.
        if (updated.DebtDueDate != null) c.DebtReminderDone = false;
        await _db.SaveChangesAsync();
        return Ok(c);
    }

    // ─────────────────────────────────────────────────────────────
    //  BOSHLANG'ICH QARZ ESLATMASI
    //  Undirish (eslatish) sanasi bugun yoki o'tib ketgan, qarzi hali bor
    //  va ko'rib chiqilmagan mijozlar. Admin/Buxgalter paneli ochilganda
    //  banner shu mijozlardan ham xabar beradi (firma eslatmalari bilan birga).
    // ─────────────────────────────────────────────────────────────
    [HttpGet("due")]
    public async Task<IActionResult> Due()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        var list = await _db.Clients
            .Where(c => c.DebtDueDate != null
                        && c.DebtDueDate < tomorrow
                        && !c.DebtReminderDone
                        && c.DebtBalance > 0.5)
            .OrderBy(c => c.DebtDueDate)
            .ToListAsync();
        return Ok(list);
    }

    // Boshlang'ich qarz eslatmasini "ko'rib chiqildi" deb belgilash.
    [HttpPost("{id}/ack")]
    public async Task<IActionResult> AckReminder(int id)
    {
        var c = await _db.Clients.FindAsync(id);
        if (c == null) return NotFound();
        c.DebtReminderDone = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await _db.Clients.FindAsync(id);
        if (c == null) return NotFound();
        _db.Clients.Remove(c);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ─────────────────────────────────────────────────────────────
    //  QARZ TO'LASH
    //  Mijoz qarzini qisman yoki to'liq to'laydi. DebtBalance kamayadi
    //  va to'lov tarixga yoziladi (summa, qolgan qarz, sana).
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/payments")]
    public async Task<IActionResult> PayDebt(int id, [FromBody] DebtPaymentRequest req)
    {
        var client = await _db.Clients.FindAsync(id);
        if (client == null) return NotFound();

        double amount = req?.Amount ?? 0;
        if (amount <= 0)
            return BadRequest(new { message = "To'lov summasi 0 dan katta bo'lishi kerak." });
        if (client.DebtBalance <= 0.5)
            return BadRequest(new { message = "Bu mijozda qarz yo'q." });

        // Qarzdan ortiq to'lansa — faqat qarz miqdoricha hisoblaymiz (qarz manfiy bo'lmaydi)
        double applied = Math.Min(amount, client.DebtBalance);
        client.DebtBalance = Math.Round(client.DebtBalance - applied, 2);
        // Tiyin qoldiqlari (1 so'mdan kam) "qarz bor" bo'lib qolmasligi uchun to'liq tozalaymiz.
        if (client.DebtBalance < 1) client.DebtBalance = 0;
        // Qarz to'liq yopilsa — boshlang'ich qarz eslatmasi ham keraksiz bo'ladi.
        if (client.DebtBalance <= 0.5) client.DebtReminderDone = true;

        var payment = new DebtPayment
        {
            ClientId = id,
            Amount = applied,
            RemainingAfter = client.DebtBalance,
            PaidAt = DateTime.UtcNow,
            Note = string.IsNullOrWhiteSpace(req?.Note) ? null : req!.Note!.Trim()
        };
        _db.DebtPayments.Add(payment);
        await _db.SaveChangesAsync();

        return Ok(new { payment, client });
    }

    // Mijozning qarz to'lovlari tarixi (eng yangisi birinchi)
    [HttpGet("{id}/payments")]
    public async Task<IActionResult> GetPayments(int id)
    {
        var list = await _db.DebtPayments
            .Where(p => p.ClientId == id)
            .OrderByDescending(p => p.PaidAt)
            .ToListAsync();
        return Ok(list);
    }
}

public class DebtPaymentRequest
{
    public double Amount { get; set; }
    public string? Note { get; set; }
}