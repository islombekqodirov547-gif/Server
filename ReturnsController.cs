using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

// ═══════════════════════════════════════════════════════════════════
//  VOZVRAT (QAYTARISH) KONTROLLERI
//  Mijoz ilgari olgan mahsulotni qaytarsa — kassir shu chek bo'yicha
//  qaytarishni rasmiylashtiradi. Tizim:
//    • Qaytarilgan mahsulotni OMBORGA qaytaradi (TotalPieces += qty).
//    • Pulni qaytaradi (naqd/plastik) yoki chek qarzga bo'lsa qarzdan
//      ayiradi (DebtReduced) — bu DebtPayment sifatida tarixga yoziladi.
//    • Asl chekning ReturnedSum'ini oshiradi (audit, qisman/ko'p martalik
//      qaytarishlarni to'g'ri kuzatish uchun).
//  Barcha o'zgarishlar BITTA tranzaksiyada — yarim qolgan holat bo'lmaydi.
// ═══════════════════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
public class ReturnsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReturnsController(AppDbContext db) => _db = db;

    // ─────────────────────────────────────────────────────────────
    //  1) Chek bo'yicha qaytarish mumkin bo'lgan qatorlarni olish.
    //     Kassir "Vozvrat" tugmasini bosganda shu ro'yxat ochiladi:
    //     har bir mahsulot uchun nechta olingan, nechta allaqachon
    //     qaytarilgan va nechta qaytarish mumkinligi ko'rsatiladi.
    // ─────────────────────────────────────────────────────────────
    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> GetReturnable(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return NotFound("Chek topilmadi.");
        if (order.Status == "Pending")
            return BadRequest("To'lanmagan (kutilayotgan) chekdan qaytarib bo'lmaydi.");

        // Shu chek bo'yicha har bir buyurtma qatoridan allaqachon qaytarilgan miqdor
        var returnedByItem = await _db.ReturnItems
            .Where(ri => ri.Return!.OrderId == orderId)
            .GroupBy(ri => ri.OrderItemId)
            .Select(g => new { OrderItemId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToListAsync();

        var lines = order.Items.Select(it =>
        {
            int already = returnedByItem.FirstOrDefault(r => r.OrderItemId == it.Id)?.Qty ?? 0;
            return new ReturnableLineDto
            {
                OrderItemId = it.Id,
                ProductId = it.ProductId,
                ProductName = it.Product?.Name ?? $"#{it.ProductId}",
                Unit = it.Product?.Unit ?? "dona",
                Price = it.Price,
                Purchased = it.Quantity,
                AlreadyReturned = already,
                Returnable = Math.Max(0, it.Quantity - already)
            };
        }).ToList();

        // Shu chekning to'lov holatiga qarab tavsiya etilgan qaytarish turi
        double netTotal = Math.Max(0, order.TotalSum - order.ReturnedSum);
        double orderDebtRemaining = Math.Max(0, netTotal - order.PaidSum);

        return Ok(new ReturnableOrderDto
        {
            OrderId = order.Id,
            ClientId = order.ClientId,
            ClientName = order.Client?.Name ?? "Naqd xaridor",
            ClientDebt = order.Client?.DebtBalance ?? 0,
            PaymentType = order.PaymentType,
            TotalSum = order.TotalSum,
            PaidSum = order.PaidSum,
            ReturnedSum = order.ReturnedSum,
            OrderDebtRemaining = orderDebtRemaining,
            CreatedAt = order.CreatedAt,
            Lines = lines
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  1.b) Bitta chek bo'yicha QILINGAN barcha qaytarishlar ro'yxati.
    //       Kassir "Mening sotuvlarim" dan qaytarilgan chekni qayta chop
    //       etganda — asl chek + barcha vozvratlarni birga ko'rsatish uchun.
    // ─────────────────────────────────────────────────────────────
    [HttpGet("order/{orderId}/list")]
    public async Task<IActionResult> GetByOrder(int orderId)
    {
        var list = await _db.Returns
            .Where(r => r.OrderId == orderId)
            .Include(r => r.Client)
            .Include(r => r.Cashier)
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
        return Ok(list);
    }

    // ─────────────────────────────────────────────────────────────
    //  2) Qaytarishni rasmiylashtirish.
    // ─────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReturnRequest req)
    {
        if (req == null) return BadRequest("So'rov bo'sh.");
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest("Qaytariladigan mahsulot tanlanmagan.");

        // ── IDEMPOTENTLIK (offline vozvrat uchun) ─────────────────────
        //  Qaytarish offline qilingan bo'lsa, server qaytganda YAGONA ClientOpId
        //  bilan yuboriladi. Takror yuborilsa — stok/qarz IKKI MARTA o'zgarmaydi.
        var retOpId = (req.ClientOpId ?? "").Trim();
        if (retOpId.Length > 0)
        {
            var prev = await _db.SyncOperations.FirstOrDefaultAsync(s => s.OperationId == retOpId);
            if (prev != null)
            {
                var dupRet = await _db.Returns
                    .Include(r => r.Order).Include(r => r.Client).Include(r => r.Cashier)
                    .Include(r => r.Items).ThenInclude(i => i.Product)
                    .FirstOrDefaultAsync(r => r.Id == prev.EntityId);
                if (dupRet != null) return Ok(dupRet);
                return Ok(new { duplicate = true, operationId = retOpId });
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var order = await _db.Orders
                .Include(o => o.Client)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.Id == req.OrderId);

            if (order == null) return NotFound("Chek topilmadi.");
            if (order.Status == "Pending")
                return BadRequest("To'lanmagan (kutilayotgan) chekdan qaytarib bo'lmaydi.");

            // Shu chek bo'yicha har bir qatordan allaqachon qaytarilgan miqdor
            var alreadyMap = (await _db.ReturnItems
                    .Where(ri => ri.Return!.OrderId == order.Id)
                    .GroupBy(ri => ri.OrderItemId)
                    .Select(g => new { OrderItemId = g.Key, Qty = g.Sum(x => x.Quantity) })
                    .ToListAsync())
                .ToDictionary(x => x.OrderItemId, x => x.Qty);

            var ret = new Return
            {
                OrderId = order.Id,
                ClientId = order.ClientId,
                CashierId = (req.CashierId.HasValue && req.CashierId.Value > 0) ? req.CashierId : null,
                Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason!.Trim(),
                CreatedAt = (req.OccurredAt.HasValue && req.OccurredAt.Value != default)
                    ? req.OccurredAt.Value.ToUniversalTime()   // offline qaytarish vaqtini saqlaymiz
                    : DateTime.UtcNow,
                Items = new List<ReturnItem>()
            };

            double total = 0;
            foreach (var reqItem in req.Items)
            {
                if (reqItem.Quantity <= 0) continue;

                var orderItem = order.Items.FirstOrDefault(i => i.Id == reqItem.OrderItemId);
                if (orderItem == null)
                    return BadRequest($"Chek qatori topilmadi (OrderItemId={reqItem.OrderItemId}).");

                int already = alreadyMap.TryGetValue(orderItem.Id, out var q) ? q : 0;
                int returnable = orderItem.Quantity - already;
                if (reqItem.Quantity > returnable)
                {
                    string nm = orderItem.Product?.Name ?? $"#{orderItem.ProductId}";
                    return BadRequest($"'{nm}' uchun qaytarish mumkin bo'lgan miqdordan oshib ketdi. " +
                                      $"Qaytarish mumkin: {returnable}.");
                }

                // Mahsulotni omborga qaytaramiz
                var product = orderItem.Product ?? await _db.Products.FindAsync(orderItem.ProductId);
                if (product != null) product.TotalPieces += reqItem.Quantity;

                double linePrice = orderItem.Price;
                total += reqItem.Quantity * linePrice;

                ret.Items.Add(new ReturnItem
                {
                    OrderItemId = orderItem.Id,
                    ProductId = orderItem.ProductId,
                    Quantity = reqItem.Quantity,
                    Price = linePrice
                });
            }

            if (ret.Items.Count == 0)
                return BadRequest("Qaytariladigan miqdor 0. Hech narsa qaytarilmadi.");

            total = Math.Round(total);

            // ── PUL TAQSIMOTI ────────────────────────────────────────
            //  Agar chek qarzga bo'lsa (mijoz hali to'lamagan qism bor) — avval
            //  o'sha qarzni kamaytiramiz (mijoz qaytargan mahsulot uchun qarz
            //  qolmaydi). Qolgan summa (mijoz haqiqatda to'lagan qism) naqd yoki
            //  plastik qilib qaytariladi.
            double debtReduced = 0;
            double netTotalBefore = Math.Max(0, order.TotalSum - order.ReturnedSum);
            double orderDebtRemaining = Math.Max(0, netTotalBefore - order.PaidSum);

            if (order.ClientId.HasValue && order.Client != null && orderDebtRemaining > 0.5)
            {
                debtReduced = Math.Min(total, Math.Min(orderDebtRemaining, order.Client.DebtBalance));
                debtReduced = Math.Round(Math.Max(0, debtReduced));
                if (debtReduced > 0)
                {
                    order.Client.DebtBalance = Math.Round(order.Client.DebtBalance - debtReduced);
                    if (order.Client.DebtBalance < 1) order.Client.DebtBalance = 0;

                    string cashierName = "";
                    if (ret.CashierId.HasValue)
                    {
                        var cashier = await _db.Users.FindAsync(ret.CashierId.Value);
                        cashierName = cashier?.FullName ?? "";
                    }

                    // Qarz tarixiga yozamiz (admin panelida qancha, qachon, qaysi
                    // chek bo'yicha qarz kamayganini ko'rsatish uchun).
                    _db.DebtPayments.Add(new DebtPayment
                    {
                        ClientId = order.ClientId.Value,
                        Amount = debtReduced,
                        RemainingAfter = order.Client.DebtBalance,
                        PaidAt = DateTime.UtcNow,
                        Note = $"↩️ Vozvrat (chek #{order.Id}) — qarzdan ayirildi" +
                               (string.IsNullOrEmpty(cashierName) ? "" : $", kassir: {cashierName}")
                    });
                }
            }

            // Naqd/plastik qaytariladigan qism
            double cashRefundable = Math.Round(total - debtReduced);
            if (cashRefundable < 0) cashRefundable = 0;

            string refundType = (req.RefundType ?? "Cash").Trim();
            double cashRefund = 0, cardRefund = 0;
            if (refundType.Equals("Card", StringComparison.OrdinalIgnoreCase))
                cardRefund = cashRefundable;
            else
                cashRefund = cashRefundable;

            // Yakuniy tur belgisi (chek va statistikada ko'rinishi uchun)
            string finalType;
            if (debtReduced > 0.5 && cashRefundable > 0.5)
                finalType = "Mixed";
            else if (debtReduced > 0.5)
                finalType = "Debt";
            else if (cardRefund > 0.5)
                finalType = "Card";
            else
                finalType = "Cash";

            ret.TotalSum = total;
            ret.DebtReduced = debtReduced;
            ret.CashRefund = cashRefund;
            ret.CardRefund = cardRefund;
            ret.RefundType = finalType;

            // Asl chekning qaytarilgan summasini oshiramiz (audit/marker)
            order.ReturnedSum = Math.Round(order.ReturnedSum + total);

            _db.Returns.Add(ret);
            await _db.SaveChangesAsync();

            // Idempotentlik yozuvi — bu vozvrat boshqa qayta qo'llanmaydi.
            if (retOpId.Length > 0)
            {
                _db.SyncOperations.Add(new SyncOperation
                {
                    OperationId = retOpId,
                    Type = "Return",
                    EntityId = ret.Id,
                    Amount = total,
                    AppliedAmount = total,
                    Note = "Offline vozvrat (kassa)",
                    ClientCreatedAt = ret.CreatedAt,
                    AppliedAt = DateTime.UtcNow,
                    Device = string.IsNullOrWhiteSpace(req.Device) ? null : req.Device!.Trim()
                });
                await _db.SaveChangesAsync();
            }

            await tx.CommitAsync();

            var created = await _db.Returns
                .Include(r => r.Order)
                .Include(r => r.Client)
                .Include(r => r.Cashier)
                .Include(r => r.Items).ThenInclude(i => i.Product)
                .FirstAsync(r => r.Id == ret.Id);

            return Ok(created);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, "Qaytarishda xatolik: " + ex.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  3) Qaytarishlar tarixi (admin/kassir uchun).
    // ─────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetHistory([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _db.Returns
            .Include(r => r.Client)
            .Include(r => r.Cashier)
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .AsQueryable();

        if (from.HasValue) query = query.Where(r => r.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(r => r.CreatedAt <= to.Value.AddDays(1));

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return Ok(list);
    }
}

// ── So'rov / javob DTO'lari ──────────────────────────────────────
public class ReturnRequest
{
    public int OrderId { get; set; }
    public int? CashierId { get; set; }
    public string RefundType { get; set; } = "Cash";   // naqd/plastik qaytariladigan qism uchun
    public string? Reason { get; set; }
    public List<ReturnItemRequest> Items { get; set; } = new();

    // ── Offline/idempotentlik (ixtiyoriy) ─────────────────────────
    public string? ClientOpId { get; set; }   // YAGONA op id — takror yuborilsa bir marta qo'llanadi
    public DateTime? OccurredAt { get; set; }  // qaytarish aslida (offline) bo'lib o'tgan vaqt
    public string? Device { get; set; }        // qaysi kassa kompyuteri (audit)
}

public class ReturnItemRequest
{
    public int OrderItemId { get; set; }
    public int Quantity { get; set; }
}

public class ReturnableOrderDto
{
    public int OrderId { get; set; }
    public int? ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public double ClientDebt { get; set; }
    public string PaymentType { get; set; } = "";
    public double TotalSum { get; set; }
    public double PaidSum { get; set; }
    public double ReturnedSum { get; set; }
    public double OrderDebtRemaining { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ReturnableLineDto> Lines { get; set; } = new();
}

public class ReturnableLineDto
{
    public int OrderItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "dona";
    public double Price { get; set; }
    public int Purchased { get; set; }
    public int AlreadyReturned { get; set; }
    public int Returnable { get; set; }
}