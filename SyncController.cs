using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

// ═══════════════════════════════════════════════════════════════════
//  SINXRON (offline-first) — BOSHLIQ MOBIL ILOVASI UCHUN
//  ───────────────────────────────────────────────────────────────────
//  Ish tartibi:
//    1) Ertalab do'konda (WiFi) — ilova GET /api/Sync/snapshot ni chaqirib,
//       barcha mijoz/firma/mahsulot ma'lumotini telefonga yuklab oladi.
//    2) Boshliq ko'chada (internetsiz) — qarz yig'adi, firmaga to'laydi.
//       Har bir amal telefonda navbatga (queue) yoziladi, YAGONA OperationId
//       (GUID) bilan.
//    3) Do'konga qaytib — POST /api/Sync/push: navbatdagi barcha amallarni
//       serverga yuboradi. Server ularni QO'LLAYDI (qarzni kamaytiradi,
//       to'lov tarixiga yozadi) va eng yangi snapshotni qaytaradi.
//
//  IDEMPOTENTLIK: har bir amal OperationId orqali bir martagina qo'llanadi.
//  Tarmoq uzilib qayta yuborilsa yoki boshliq ikki marta bossa ham — qarz
//  IKKI BAROBAR kamaymaydi. Bu eng muhim xavfsizlik kafolati.
//
//  KELISHMOVCHILIK (conflict): boshliq ko'chada bo'lganda do'konda yangi
//  qarzga savdo bo'lsa — mijoz qarzi serverda oshadi. Push paytida boshliq
//  yiqqan pul shu (yangi) qarzdan kamaytiriladi va qaytgan snapshot to'g'ri
//  yakuniy holatni ko'rsatadi. Shunday qilib ikki tomon to'liq teng bo'ladi.
// ═══════════════════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly AppDbContext _db;
    public SyncController(AppDbContext db) => _db = db;

    private const string TypeClientPayment = "ClientPayment";
    private const string TypeSupplierPayment = "SupplierPayment";

    // ─────────────────────────────────────────────────────────────
    //  1) SNAPSHOT — joriy holatni to'liq yuklab olish
    // ─────────────────────────────────────────────────────────────
    [HttpGet("snapshot")]
    public async Task<IActionResult> Snapshot()
    {
        var snap = await BuildSnapshotAsync();
        return Ok(snap);
    }

    // ─────────────────────────────────────────────────────────────
    //  2) PUSH — offline amallarni serverga yuborish (idempotent)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("push")]
    public async Task<IActionResult> Push([FromBody] SyncPushRequest? req)
    {
        var results = new List<SyncOpResult>();

        if (req?.Operations == null || req.Operations.Count == 0)
        {
            // Amallar bo'lmasa ham — eng yangi snapshotni qaytaramiz (faqat "pull").
            return Ok(new SyncPushResponse
            {
                ServerTime = DateTime.UtcNow,
                Results = results,
                Snapshot = await BuildSnapshotAsync()
            });
        }

        // Amallarni qurilmadagi bajarilish vaqti bo'yicha tartiblaymiz — bir
        // mijozga ketma-ket to'lovlar to'g'ri (ketma-ket) qo'llanishi uchun.
        var ordered = req.Operations
            .Where(o => o != null && !string.IsNullOrWhiteSpace(o.OperationId))
            .OrderBy(o => o.CreatedAt)
            .ToList();

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var op in ordered)
            {
                var res = await ApplyOperationAsync(op, req.Device);
                results.Add(res);
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { message = "Sinxronlashda xatolik: " + ex.Message });
        }

        // Qo'llangandan keyin — eng yangi snapshot (do'kondagi savdolar ham aks etadi)
        var response = new SyncPushResponse
        {
            ServerTime = DateTime.UtcNow,
            Results = results,
            Snapshot = await BuildSnapshotAsync()
        };
        return Ok(response);
    }

    // ── Bitta amalni qo'llash ────────────────────────────────────
    private async Task<SyncOpResult> ApplyOperationAsync(SyncOpRequest op, string? device)
    {
        var type = (op.Type ?? "").Trim();

        // 1) Takror tekshiruvi (idempotentlik): bu OperationId allaqachon qo'llanganmi?
        var existing = await _db.SyncOperations
            .FirstOrDefaultAsync(s => s.OperationId == op.OperationId);
        if (existing != null)
        {
            return new SyncOpResult
            {
                OperationId = op.OperationId,
                Status = "duplicate",
                AppliedAmount = existing.AppliedAmount,
                Message = "Bu amal allaqachon qo'llangan (takror qabul qilinmadi)."
            };
        }

        double amount = Math.Round(op.Amount);
        if (amount <= 0)
        {
            return new SyncOpResult
            {
                OperationId = op.OperationId,
                Status = "skipped",
                AppliedAmount = 0,
                Message = "Summa 0 dan katta bo'lishi kerak."
            };
        }

        if (type == TypeClientPayment)
            return await ApplyClientPaymentAsync(op, amount, device);
        if (type == TypeSupplierPayment)
            return await ApplySupplierPaymentAsync(op, amount, device);

        return new SyncOpResult
        {
            OperationId = op.OperationId,
            Status = "error",
            AppliedAmount = 0,
            Message = $"Noma'lum amal turi: '{type}'."
        };
    }

    // ── Mijoz qarzini to'lash ────────────────────────────────────
    private async Task<SyncOpResult> ApplyClientPaymentAsync(SyncOpRequest op, double amount, string? device)
    {
        var client = await _db.Clients.FindAsync(op.EntityId);
        if (client == null)
        {
            return new SyncOpResult
            {
                OperationId = op.OperationId,
                Status = "error",
                AppliedAmount = 0,
                Message = "Mijoz topilmadi (o'chirilgan bo'lishi mumkin)."
            };
        }

        // Qarzdan ortiq to'lansa — faqat qarz miqdoricha qo'llaymiz (qarz manfiy bo'lmaydi).
        double applied = Math.Min(amount, Math.Max(0, client.DebtBalance));
        client.DebtBalance = Math.Round(client.DebtBalance - applied);
        if (client.DebtBalance < 1) client.DebtBalance = 0;

        // To'lov tarixiga yozamiz (admin panelida ko'rinadi)
        _db.DebtPayments.Add(new DebtPayment
        {
            ClientId = client.Id,
            Amount = applied,
            RemainingAfter = client.DebtBalance,
            PaidAt = DateTime.UtcNow,
            Note = ComposeNote(op.Note, "Boshliq (mobil)")
        });

        RecordOperation(op, applied, device);

        return new SyncOpResult
        {
            OperationId = op.OperationId,
            Status = "applied",
            AppliedAmount = applied,
            Remaining = client.DebtBalance,
            Message = applied < amount
                ? "Qarzdan ortiq summa kiritildi — faqat qarz miqdoricha qo'llandi."
                : null
        };
    }

    // ── Firmaga to'lash ──────────────────────────────────────────
    private async Task<SyncOpResult> ApplySupplierPaymentAsync(SyncOpRequest op, double amount, string? device)
    {
        var supplier = await _db.Suppliers.FindAsync(op.EntityId);
        if (supplier == null)
        {
            return new SyncOpResult
            {
                OperationId = op.OperationId,
                Status = "error",
                AppliedAmount = 0,
                Message = "Firma topilmadi (o'chirilgan bo'lishi mumkin)."
            };
        }

        double applied = Math.Min(amount, Math.Max(0, supplier.DebtBalance));
        supplier.DebtBalance = Math.Round(supplier.DebtBalance - applied);
        if (supplier.DebtBalance < 1) supplier.DebtBalance = 0;

        _db.SupplierPayments.Add(new SupplierPayment
        {
            SupplierId = supplier.Id,
            Amount = applied,
            RemainingAfter = supplier.DebtBalance,
            PaidAt = DateTime.UtcNow,
            Note = ComposeNote(op.Note, "Boshliq (mobil)")
        });

        RecordOperation(op, applied, device);

        return new SyncOpResult
        {
            OperationId = op.OperationId,
            Status = "applied",
            AppliedAmount = applied,
            Remaining = supplier.DebtBalance,
            Message = applied < amount
                ? "Qarzdan ortiq summa kiritildi — faqat qarz miqdoricha qo'llandi."
                : null
        };
    }

    // Amalni SyncOperations jadvaliga yozib qo'yamiz (qayta qo'llanmasligi uchun).
    private void RecordOperation(SyncOpRequest op, double applied, string? device)
    {
        _db.SyncOperations.Add(new SyncOperation
        {
            OperationId = op.OperationId,
            Type = (op.Type ?? "").Trim(),
            EntityId = op.EntityId,
            Amount = Math.Round(op.Amount),
            AppliedAmount = applied,
            Note = string.IsNullOrWhiteSpace(op.Note) ? null : op.Note!.Trim(),
            ClientCreatedAt = op.CreatedAt == default ? DateTime.UtcNow : op.CreatedAt,
            AppliedAt = DateTime.UtcNow,
            Device = string.IsNullOrWhiteSpace(device) ? null : device!.Trim()
        });
    }

    private static string ComposeNote(string? userNote, string source)
    {
        var n = (userNote ?? "").Trim();
        return string.IsNullOrEmpty(n) ? source : $"{source}: {n}";
    }

    // ── Snapshot quruvchi ────────────────────────────────────────
    private async Task<SyncSnapshot> BuildSnapshotAsync()
    {
        var clients = await _db.Clients
            .OrderBy(c => c.Name)
            .Select(c => new SnapClient
            {
                Id = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                DebtBalance = c.DebtBalance
            })
            .ToListAsync();

        var suppliers = await _db.Suppliers
            .OrderBy(s => s.Name)
            .Select(s => new SnapSupplier
            {
                Id = s.Id,
                Name = s.Name,
                Phone = s.Phone,
                Note = s.Note,
                DebtBalance = s.DebtBalance
            })
            .ToListAsync();

        var products = await _db.Products
            .OrderBy(p => p.Name)
            .Select(p => new SnapProduct
            {
                Id = p.Id,
                Name = p.Name,
                Unit = p.Unit,
                QuantityInBlock = p.QuantityInBlock,
                BuyPriceBlock = p.BuyPriceBlock,
                SellPriceBlock = p.SellPriceBlock,
                SellPricePiece = p.SellPricePiece,
                TotalPieces = p.TotalPieces
            })
            .ToListAsync();

        return new SyncSnapshot
        {
            ServerTime = DateTime.UtcNow,
            Clients = clients,
            Suppliers = suppliers,
            Products = products
        };
    }
}

// ─────────────────────────  DTO'lar  ─────────────────────────

public class SyncPushRequest
{
    public string? Device { get; set; }
    public List<SyncOpRequest> Operations { get; set; } = new();
}

public class SyncOpRequest
{
    public string OperationId { get; set; } = "";
    public string Type { get; set; } = "";          // ClientPayment | SupplierPayment
    public int EntityId { get; set; }                // ClientId yoki SupplierId
    public double Amount { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }          // qurilmada bajarilgan vaqt
}

public class SyncPushResponse
{
    public DateTime ServerTime { get; set; }
    public List<SyncOpResult> Results { get; set; } = new();
    public SyncSnapshot Snapshot { get; set; } = new();
}

public class SyncOpResult
{
    public string OperationId { get; set; } = "";
    public string Status { get; set; } = "";         // applied | duplicate | skipped | error
    public double AppliedAmount { get; set; }
    public double Remaining { get; set; }
    public string? Message { get; set; }
}

public class SyncSnapshot
{
    public DateTime ServerTime { get; set; }
    public List<SnapClient> Clients { get; set; } = new();
    public List<SnapSupplier> Suppliers { get; set; } = new();
    public List<SnapProduct> Products { get; set; } = new();
}

public class SnapClient
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public double DebtBalance { get; set; }
}

public class SnapSupplier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public double DebtBalance { get; set; }
}

public class SnapProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "dona";
    public int QuantityInBlock { get; set; }
    public double BuyPriceBlock { get; set; }
    public double SellPriceBlock { get; set; }
    public double SellPricePiece { get; set; }
    public int TotalPieces { get; set; }
}