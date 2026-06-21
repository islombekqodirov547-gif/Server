using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

// Kirim (oluv) — firmadan mahsulot kelishi. Kirim yaratilganda:
//  • har bir mahsulot ombori (TotalPieces) oshadi,
//  • mahsulot xarid narxi (BuyPriceBlock) yangilanadi (ixtiyoriy),
//  • to'lanmagan qism firmaning qarziga (DebtBalance) qo'shiladi.
[ApiController]
[Route("api/[controller]")]
public class PurchasesController : ControllerBase
{
    private readonly AppDbContext _db;
    public PurchasesController(AppDbContext db) => _db = db;

    // Kirimlar ro'yxati (ixtiyoriy sana oralig'i bilan), eng yangisi birinchi
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var q = _db.Purchases
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .AsQueryable();

        if (from.HasValue) q = q.Where(p => p.CreatedAt >= from.Value.Date);
        if (to.HasValue) q = q.Where(p => p.CreatedAt < to.Value.Date.AddDays(1));

        var list = await q.OrderByDescending(p => p.CreatedAt).ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var p = await _db.Purchases
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(p => p.Id == id);
        return p == null ? NotFound() : Ok(p);
    }

    // Kirimlar bo'yicha qisqa hisobot (sana oralig'i)
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var q = _db.Purchases.AsQueryable();
        if (from.HasValue) q = q.Where(p => p.CreatedAt >= from.Value.Date);
        if (to.HasValue) q = q.Where(p => p.CreatedAt < to.Value.Date.AddDays(1));

        var list = await q.ToListAsync();
        double totalDebt = await _db.Suppliers.SumAsync(s => (double?)s.DebtBalance) ?? 0;

        return Ok(new
        {
            count = list.Count,
            totalPurchased = list.Sum(p => p.TotalSum),
            totalPaid = list.Sum(p => p.PaidSum),
            periodDebt = list.Sum(p => p.TotalSum - p.PaidSum),
            supplierDebtTotal = totalDebt
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  QARZ MUDDATI ESLATMASI
    //  To'lash muddati (DueDate) bugun yoki o'tib ketgan, hali to'lanmagan
    //  (firma qarzi bor) va buxgalter/admin tomonidan "ko'rib chiqilmagan"
    //  kirimlar. Admin/Buxgalter paneli ochilganda banner shulardan xabar beradi.
    // ─────────────────────────────────────────────────────────────
    [HttpGet("due")]
    public async Task<IActionResult> Due()
    {
        // Bugun tugaguncha (ertaga 00:00 dan oldin) muddati kelganlar
        var tomorrow = DateTime.Today.AddDays(1);

        var list = await _db.Purchases
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .Where(p => p.DueDate != null
                        && p.DueDate < tomorrow
                        && !p.ReminderDone
                        && (p.TotalSum - p.PaidSum) > 0.5
                        && p.Supplier != null
                        && p.Supplier.DebtBalance > 0.5)
            .OrderBy(p => p.DueDate)
            .ToListAsync();

        return Ok(list);
    }

    // Eslatmani "ko'rib chiqildi" deb belgilash (Tushunarli yoki to'langandan keyin).
    [HttpPost("{id}/ack")]
    public async Task<IActionResult> AckReminder(int id)
    {
        var p = await _db.Purchases.FindAsync(id);
        if (p == null) return NotFound();
        p.ReminderDone = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ─────────────────────────────────────────────────────────────
    //  YANGI KIRIM YARATISH (tranzaksiya: hammasi yoki hech narsa)
    // ─────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PurchaseRequest req)
    {
        if (req == null || req.Items == null || req.Items.Count == 0)
            return BadRequest(new { message = "Kamida bitta mahsulot qatori bo'lishi kerak." });

        var supplier = await _db.Suppliers.FindAsync(req.SupplierId);
        if (supplier == null)
            return BadRequest(new { message = "Firma tanlanmagan yoki topilmadi." });

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var purchase = new Purchase
            {
                SupplierId = supplier.Id,
                CreatedAt = DateTime.UtcNow,
                DueDate = req.DueDate,
                Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note!.Trim()
            };

            double total = 0;
            foreach (var it in req.Items)
            {
                if (it.Quantity <= 0) continue;
                var product = await _db.Products.FindAsync(it.ProductId);
                if (product == null) continue;

                // PiecesPerBlock — har bir "Quantity" birligi nechta donaga teng.
                //  • Berilsa (override) — o'shani ishlatamiz (masalan dona rejimida 1).
                //  • Berilmasa — mahsulotning blokdagi dona soni.
                // Shu sabab blok / dona / kg kirimlari aniq hisoblanadi.
                int piecesPerBlock = it.PiecesPerBlock.HasValue && it.PiecesPerBlock.Value > 0
                    ? it.PiecesPerBlock.Value
                    : (product.QuantityInBlock <= 0 ? 1 : product.QuantityInBlock);
                int piecesAdded = it.Quantity * piecesPerBlock;

                // Omborni oshiramiz
                product.TotalPieces += piecesAdded;

                // Xarid narxini yangilaymiz (ixtiyoriy). Saqlanadigan narx — doim BLOK narxi.
                //  • BuyPriceBlock berilsa — o'shani ishlatamiz (dona rejimida desktop blok narxiga aylantiradi).
                //  • Berilmasa — UnitCost (blok rejimida UnitCost = blok narxining o'zi).
                if (it.UpdateBuyPrice)
                {
                    double newBlockPrice = it.BuyPriceBlock.HasValue && it.BuyPriceBlock.Value > 0
                        ? it.BuyPriceBlock.Value
                        : it.UnitCost;
                    if (newBlockPrice > 0) product.BuyPriceBlock = newBlockPrice;
                }

                double lineTotal = it.Quantity * it.UnitCost;
                total += lineTotal;

                purchase.Items.Add(new PurchaseItem
                {
                    ProductId = product.Id,
                    Quantity = it.Quantity,
                    PiecesPerBlock = piecesPerBlock,
                    UnitCost = it.UnitCost
                });

                // Mahsulot kirim tarixiga ham yozamiz (per-product "Kirimlar tarixi")
                _db.Stocks.Add(new Stock
                {
                    ProductId = product.Id,
                    QuantityAdded = piecesAdded,
                    BuyPrice = it.UnitCost,
                    Note = $"Firma: {supplier.Name}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (purchase.Items.Count == 0)
                return BadRequest(new { message = "Yaroqli mahsulot qatori topilmadi." });

            total = Math.Round(total, 2);
            double paid = req.PaidSum;
            if (paid < 0) paid = 0;
            if (paid > total) paid = total;       // jamidan ortiq to'lab bo'lmaydi
            double debt = Math.Round(total - paid, 2);

            purchase.TotalSum = total;
            purchase.PaidSum = paid;
            purchase.Status = debt <= 0.5 ? "Paid" : (paid > 0.5 ? "Partial" : "Debt");

            // To'lanmagan qism firma qarziga qo'shiladi
            if (debt > 0.5)
                supplier.DebtBalance = Math.Round(supplier.DebtBalance + debt, 2);

            _db.Purchases.Add(purchase);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            var created = await _db.Purchases
                .Include(p => p.Supplier)
                .Include(p => p.Items).ThenInclude(i => i.Product)
                .FirstAsync(p => p.Id == purchase.Id);
            return Ok(created);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { message = "Kirimni saqlashda xatolik: " + ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  KIRIMNI O'CHIRISH (orqaga qaytarish: ombor va qarzni tiklaydi)
    // ─────────────────────────────────────────────────────────────
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var purchase = await _db.Purchases
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (purchase == null) return NotFound();

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Omborni qaytaramiz (qo'shilgan donalarni ayiramiz)
            foreach (var it in purchase.Items)
            {
                var product = await _db.Products.FindAsync(it.ProductId);
                if (product != null)
                    product.TotalPieces -= it.Quantity * (it.PiecesPerBlock <= 0 ? 1 : it.PiecesPerBlock);
            }

            // Firma qarzini kamaytiramiz (bu kirimning to'lanmagan qismi qancha bo'lsa)
            double debt = Math.Round(purchase.TotalSum - purchase.PaidSum, 2);
            if (debt > 0.5)
            {
                var supplier = await _db.Suppliers.FindAsync(purchase.SupplierId);
                if (supplier != null)
                {
                    supplier.DebtBalance = Math.Round(supplier.DebtBalance - debt, 2);
                    if (supplier.DebtBalance < 0) supplier.DebtBalance = 0;
                }
            }

            _db.Purchases.Remove(purchase); // qatorlari cascade bilan o'chadi
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { message = "Kirimni o'chirishda xatolik: " + ex.Message });
        }
    }
}

public class PurchaseRequest
{
    public int SupplierId { get; set; }
    public double PaidSum { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Note { get; set; }
    public List<PurchaseItemRequest> Items { get; set; } = new();
}

public class PurchaseItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }       // necha blok/birlik olindi
    public double UnitCost { get; set; }    // 1 blok/birlik xarid narxi (LineTotal shu bo'yicha)
    public bool UpdateBuyPrice { get; set; } = true;

    // ── Aniq hisob uchun ixtiyoriy override'lar (orqaga moslik saqlanadi) ──
    // PiecesPerBlock: har bir Quantity birligi nechta donaga teng (dona rejimida 1).
    //   null bo'lsa — mahsulotning QuantityInBlock'i ishlatiladi.
    public int? PiecesPerBlock { get; set; }
    // BuyPriceBlock: mahsulotga saqlanadigan BLOK xarid narxi (dona rejimida
    //   desktop dona narxini blok narxiga aylantirib yuboradi). null bo'lsa UnitCost.
    public double? BuyPriceBlock { get; set; }
}
