using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProductsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var products = await _db.Products
            .Include(p => p.Barcodes)
            .OrderBy(p => p.Name)
            .ToListAsync();

        // Har bir mahsulotning OXIRGI kirim qilingan firmasini bitta so'rovda topamiz.
        // (PurchaseItem -> Purchase -> Supplier; eng yangi CreatedAt bo'yicha.)
        var purchaseItems = await _db.PurchaseItems
            .Include(i => i.Purchase).ThenInclude(p => p!.Supplier)
            .Where(i => i.Purchase != null && i.Purchase.Supplier != null)
            .ToListAsync();

        var lastByProduct = purchaseItems
            .GroupBy(i => i.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(i => i.Purchase!.CreatedAt).First().Purchase!.Supplier!.Name);

        foreach (var p in products)
            if (lastByProduct.TryGetValue(p.Id, out var name))
                p.LastSupplierName = name;

        return Ok(products);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var p = await _db.Products.Include(p => p.Barcodes).FirstOrDefaultAsync(p => p.Id == id);
        return p == null ? NotFound() : Ok(p);
    }

    // ─────────────────────────────────────────────────────────────
    //  SHTRIX-KOD BO'YICHA MAHSULOT TOPISH
    //  Buxgalter (kirim) va Kassir (sotuv) skaner qilganda ishlatiladi.
    //  Kod topilsa — mahsulot (barcha kodlari bilan) qaytadi.
    //  Topilmasa — 404 (mijoz "yangi mahsulot" yoki "qo'shilmagan" deb biladi).
    // ─────────────────────────────────────────────────────────────
    [HttpGet("barcode/{code}")]
    public async Task<IActionResult> GetByBarcode(string code)
    {
        code = (code ?? "").Trim();
        if (string.IsNullOrEmpty(code)) return NotFound();

        var bc = await _db.ProductBarcodes
            .Include(b => b.Product).ThenInclude(p => p!.Barcodes)
            .FirstOrDefaultAsync(b => b.Code == code);

        if (bc?.Product == null) return NotFound();
        return Ok(bc.Product);
    }

    // ─────────────────────────────────────────────────────────────
    //  AVTOMATIK UNIKAL KOD (yangi mahsulot qo'shishda)
    //  Mahsulot qo'shish oynasi ochilganda kod maydoni avtomatik, tartib
    //  bilan, takrorlanmaydigan kod bilan to'ldiriladi:
    //    • kind=piece (dona / blok) → 1001, 1002, 1003 ...  (oraliq 1000..27999)
    //    • kind=kg                  → 28001, 28002, 28003 ... (oraliq 28000..999999)
    //  Mavjud kodlarning eng kattasidan keyingi bo'sh raqam qaytariladi.
    //  Haqiqiy (skaner) shtrix-kodlar odatda 8-13 xonali — bu kichik
    //  oraliqqa tushmaydi, shu sabab to'qnashuv bo'lmaydi.
    // ─────────────────────────────────────────────────────────────
    [HttpGet("next-code")]
    public async Task<IActionResult> NextCode([FromQuery] string kind = "piece")
    {
        bool isKg = string.Equals(kind, "kg", StringComparison.OrdinalIgnoreCase);
        long rangeStart = isKg ? 28000 : 1000;
        long rangeEnd = isKg ? 999999 : 27999;

        var codes = await _db.ProductBarcodes.Select(b => b.Code).ToListAsync();

        long max = rangeStart;
        foreach (var c in codes)
        {
            var t = (c ?? "").Trim();
            if (long.TryParse(t, out long v) && v >= rangeStart && v <= rangeEnd && v > max)
                max = v;
        }

        long next = max + 1;
        if (next > rangeEnd) next = rangeEnd;   // oraliq to'lib ketsa — chetidan oshmaymiz

        // Juda kam ehtimol: hisoblangan kod allaqachon band bo'lsa — bo'shini topamiz.
        var taken = new HashSet<string>(codes.Where(c => c != null).Select(c => c!.Trim()));
        while (next <= rangeEnd && taken.Contains(next.ToString())) next++;

        return Ok(new { code = next.ToString() });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Product product)
    {
        product.CreatedAt = DateTime.UtcNow;
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return Ok(product);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] Product updated)
    {
        var p = await _db.Products.FindAsync(id);
        if (p == null) return NotFound();
        p.Name = updated.Name;
        p.Unit = updated.Unit;
        p.QuantityInBlock = updated.QuantityInBlock;
        p.BuyPriceBlock = updated.BuyPriceBlock;
        p.SellPriceBlock = updated.SellPriceBlock;
        p.SellPricePiece = updated.SellPricePiece;
        await _db.SaveChangesAsync();
        return Ok(p);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _db.Products.FindAsync(id);
        if (p == null) return NotFound();
        _db.Products.Remove(p);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // Mahsulot bo'yicha kirimlar tarixi (sana/vaqt bilan)
    [HttpGet("{id}/stocks")]
    public async Task<IActionResult> GetStocks(int id)
    {
        var stocks = await _db.Stocks
            .Where(s => s.ProductId == id)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
        return Ok(stocks);
    }

    // ─────────────────────────────────────────────────────────────
    //  MAHSULOT OXIRGI MARTA QAYSI FIRMADAN KELGAN?
    //  Kirim qo'shganda firma maydoni avtomatik shu firmaga to'ldiriladi
    //  (buxgalter o'zgartirishi yoki yangi firma qo'shishi mumkin).
    //  Topilmasa — 204 (No Content): mahsulot hali kirim qilinmagan.
    // ─────────────────────────────────────────────────────────────
    [HttpGet("{id}/last-supplier")]
    public async Task<IActionResult> GetLastSupplier(int id)
    {
        var lastItem = await _db.PurchaseItems
            .Include(i => i.Purchase).ThenInclude(p => p!.Supplier)
            .Where(i => i.ProductId == id && i.Purchase != null && i.Purchase.Supplier != null)
            .OrderByDescending(i => i.Purchase!.CreatedAt)
            .FirstOrDefaultAsync();

        if (lastItem?.Purchase?.Supplier == null) return NoContent();
        return Ok(lastItem.Purchase.Supplier);
    }

    // ─────────────────────────────────────────────────────────────
    //  MAHSULOT QAYSI FIRMALARDAN KELGAN? (to'liq tarix)
    //  "FIRMA" ustunidagi kichik tugma bosilganda ochiladigan modal uchun:
    //  har bir kirim — firma nomi, sana, soni (blok/dona), 1 birlik narxi.
    //  Eng yangisi birinchi.
    // ─────────────────────────────────────────────────────────────
    [HttpGet("{id}/suppliers")]
    public async Task<IActionResult> GetSupplierHistory(int id)
    {
        var items = await _db.PurchaseItems
            .Include(i => i.Purchase).ThenInclude(p => p!.Supplier)
            .Where(i => i.ProductId == id && i.Purchase != null)
            .OrderByDescending(i => i.Purchase!.CreatedAt)
            .ToListAsync();

        var rows = items.Select(i => new
        {
            purchaseId = i.PurchaseId,
            supplierName = i.Purchase?.Supplier?.Name ?? "(noma'lum firma)",
            supplierPhone = i.Purchase?.Supplier?.Phone,
            createdAt = i.Purchase!.CreatedAt,
            quantity = i.Quantity,
            piecesPerBlock = i.PiecesPerBlock,
            unitCost = i.UnitCost
        }).ToList();

        return Ok(rows);
    }

    // Kirim qo'shish (mahsulot ombori to'ldirish)
    [HttpPost("{id}/addstock")]
    public async Task<IActionResult> AddStock(int id, [FromBody] StockRequest req)
    {
        var p = await _db.Products.FindAsync(id);
        if (p == null) return NotFound();

        p.TotalPieces += req.QuantityPieces;
        if (req.BuyPriceBlock > 0) p.BuyPriceBlock = req.BuyPriceBlock;

        var stock = new Stock
        {
            ProductId = id,
            QuantityAdded = req.QuantityPieces,
            BuyPrice = req.BuyPriceBlock,
            Note = req.Note,
            CreatedAt = DateTime.UtcNow
        };
        _db.Stocks.Add(stock);
        await _db.SaveChangesAsync();
        return Ok(p);
    }

    // ─────────────────────────────────────────────────────────────
    //  SHTRIX-KODLAR (bir mahsulot — ko'p kod)
    // ─────────────────────────────────────────────────────────────

    // Mahsulotning barcha kodlari
    [HttpGet("{id}/barcodes")]
    public async Task<IActionResult> GetBarcodes(int id)
    {
        var codes = await _db.ProductBarcodes
            .Where(b => b.ProductId == id)
            .OrderBy(b => b.Id)
            .ToListAsync();
        return Ok(codes);
    }

    // Mahsulotga yangi kod qo'shish.
    // Agar kod allaqachon BOSHQA mahsulotda mavjud bo'lsa — 409 (Conflict)
    // va qaysi mahsulotga tegishli ekani qaytadi (mijoz tushuntirib beradi).
    [HttpPost("{id}/barcodes")]
    public async Task<IActionResult> AddBarcode(int id, [FromBody] BarcodeRequest req)
    {
        var code = (req?.Code ?? "").Trim();
        if (string.IsNullOrEmpty(code))
            return BadRequest(new { message = "Shtrix-kod bo'sh bo'lishi mumkin emas." });

        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound();

        var existing = await _db.ProductBarcodes
            .Include(b => b.Product)
            .FirstOrDefaultAsync(b => b.Code == code);

        if (existing != null)
        {
            // Allaqachon shu mahsulotda bo'lsa — muammosiz (idempotent)
            if (existing.ProductId == id) return Ok(existing);

            return Conflict(new
            {
                message = $"Bu kod allaqachon '{existing.Product?.Name}' mahsulotiga biriktirilgan.",
                productId = existing.ProductId,
                productName = existing.Product?.Name
            });
        }

        var bc = new ProductBarcode { ProductId = id, Code = code, CreatedAt = DateTime.UtcNow };
        _db.ProductBarcodes.Add(bc);
        await _db.SaveChangesAsync();
        return Ok(bc);
    }

    // Kodni o'chirish (barcode Id bo'yicha)
    [HttpDelete("barcodes/{barcodeId}")]
    public async Task<IActionResult> DeleteBarcode(int barcodeId)
    {
        var bc = await _db.ProductBarcodes.FindAsync(barcodeId);
        if (bc == null) return NotFound();
        _db.ProductBarcodes.Remove(bc);
        await _db.SaveChangesAsync();
        return Ok();
    }
}

public class BarcodeRequest
{
    public string Code { get; set; } = "";
}

public class StockRequest
{
    public int QuantityPieces { get; set; }
    public double BuyPriceBlock { get; set; }
    public string? Note { get; set; }
}