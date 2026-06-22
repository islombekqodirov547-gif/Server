using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    public OrdersController(AppDbContext db) => _db = db;

    // Barcha buyurtmalar (kassir uchun - pending)
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var orders = await _db.Orders
            .Where(o => o.Status == "Pending")
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return Ok(orders);
    }

    // Tarix (to'langan buyurtmalar)
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _db.Orders
            .Where(o => o.Status != "Pending")
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .AsQueryable();

        if (from.HasValue) query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(o => o.CreatedAt <= to.Value.AddDays(1));

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        return Ok(orders);
    }

    // ─────────────────────────────────────────────────────────────
    //  BITTA MIJOZNING BARCHA BUYURTMALARI (kassir/admin uchun)
    //  Mijoz "men qachon nima olganman?" desa — kassir shu ro'yxatdan
    //  sana, vaqt, sotuvchi, kassir va olingan mahsulotlarni ko'rsatadi.
    // ─────────────────────────────────────────────────────────────
    [HttpGet("client/{clientId}")]
    public async Task<IActionResult> GetByClient(int clientId)
    {
        var orders = await _db.Orders
            .Where(o => o.ClientId == clientId)
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return Ok(orders);
    }

    // Statistika (admin uchun)
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var fromDate = from ?? DateTime.UtcNow.Date;
        var toDate = to?.AddDays(1) ?? DateTime.UtcNow.Date.AddDays(1);

        var orders = await _db.Orders
            .Where(o => o.Status == "Paid" && o.CreatedAt >= fromDate && o.CreatedAt < toDate)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .ToListAsync();

        double totalRevenue = orders.Sum(o => o.PaidSum);
        double totalCost = orders.SelectMany(o => o.Items)
            .Sum(i => i.Quantity * (i.Product?.BuyPriceBlock ?? 0) / (i.Product?.QuantityInBlock ?? 1));
        double profit = totalRevenue - totalCost;

        double cashTotal = orders.Sum(CashPart);
        double cardTotal = orders.Sum(CardPart);

        // ── VOZVRATLARNI HISOBDAN AYIRISH ────────────────────────────
        //  Shu davrdagi qaytarishlar tushum/foyda/naqd/plastikni kamaytiradi
        //  (mahsulot omborga qaytdi, pul mijozga qaytarildi). Qarzdan ayirilgan
        //  qism (DebtReduced) tushumga ta'sir qilmaydi — u allaqachon qarz edi.
        var returns = await _db.Returns
            .Where(r => r.CreatedAt >= fromDate && r.CreatedAt < toDate)
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .ToListAsync();

        double refundCash = returns.Sum(r => r.CashRefund);
        double refundCard = returns.Sum(r => r.CardRefund);
        double returnedCost = returns.SelectMany(r => r.Items)
            .Sum(i => i.Quantity * (i.Product?.BuyPriceBlock ?? 0) / (i.Product?.QuantityInBlock ?? 1));
        double returnTotal = returns.Sum(r => r.TotalSum);

        totalRevenue -= (refundCash + refundCard);
        totalCost -= returnedCost;
        profit = totalRevenue - totalCost;
        cashTotal -= refundCash;
        cardTotal -= refundCard;

        return Ok(new
        {
            TotalRevenue = totalRevenue,
            TotalCost = totalCost,
            Profit = profit,
            CashTotal = cashTotal,
            CardTotal = cardTotal,
            OrderCount = orders.Count,
            DebtTotal = await _db.Clients.SumAsync(c => c.DebtBalance),
            ReturnTotal = returnTotal
        });
    }

    // Yangi buyurtma yaratish (sotuvchi tomonidan)
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] OrderRequest req)
    {
        // MUHIM: hamma narsa try/catch ichida — aks holda har qanday xato XOM "500"
        // bo'lib, sababsiz qaytardi (sotuvchi ilovasida shu muammo bor edi). Endi
        // aniq sabab qaytadi va eng ko'p uchraydigan holatlar (eskirgan mijoz/mahsulot
        // id'lari) tushunarli xabar bilan ushlanadi.
        try
        {
            if (req == null) return BadRequest("So'rov bo'sh.");
            if (req.Items == null || req.Items.Count == 0)
                return BadRequest("Savatda mahsulot yo'q.");

            // ── Sotuvchi (UserId) ──
            // Yuborilgan bo'lsa, mavjudligini tekshiramiz. Topilmasa buyurtmani
            // YO'QOTMAYMIZ — sotuvchisiz (null) saqlaymiz (eskirgan sessiya bo'lishi mumkin).
            int? userId = (req.UserId.HasValue && req.UserId.Value > 0) ? req.UserId : null;
            if (userId.HasValue && !await _db.Users.AnyAsync(u => u.Id == userId.Value))
                userId = null;

            // ── Mijoz (ClientId) ──
            // Yuborilgan bo'lsa va serverda topilmasa — aniq xabar (eskirgan ro'yxat).
            int? clientId = (req.ClientId.HasValue && req.ClientId.Value > 0) ? req.ClientId : null;
            if (clientId.HasValue && !await _db.Clients.AnyAsync(c => c.Id == clientId.Value))
                return BadRequest("Tanlangan mijoz serverda topilmadi. Mijozlar ro'yxatini yangilab (pastga torting), qayta urinib ko'ring.");

            // ── Stok + summa (serverda qayta hisoblanadi) ──
            double total = 0;
            foreach (var item in req.Items)
            {
                var product = await _db.Products.FindAsync(item.ProductId);
                if (product == null)
                    return BadRequest($"Mahsulot topilmadi (id {item.ProductId}). Mahsulotlar ro'yxatini yangilang.");
                if (item.Quantity <= 0)
                    return BadRequest($"'{product.Name}' uchun miqdor noto'g'ri.");
                if (product.TotalPieces < item.Quantity)
                    return BadRequest($"'{product.Name}' uchun yetarli stok yo'q. Mavjud: {product.TotalPieces}");
                total += item.Quantity * item.Price;
            }

            var order = new Order
            {
                ClientId = clientId,
                UserId = userId,
                TotalSum = req.TotalSum > 0 ? req.TotalSum : total,
                PaidSum = 0,
                Status = "Pending",
                PaymentType = "Cash",     // to'lov turi kassirda aniqlanadi (default)
                CashAmount = 0,
                CardAmount = 0,
                CreatedAt = DateTime.UtcNow,
                Items = req.Items.Select(i => new OrderItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    Price = i.Price,
                    OriginalPrice = i.Price   // chegirmagacha asl narx (boshida = sotuv narxi)
                }).ToList()
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            // Reload with includes
            var created = await _db.Orders
                .Include(o => o.Client)
                .Include(o => o.User)
                .Include(o => o.Cashier)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .FirstAsync(o => o.Id == order.Id);

            return Ok(created);
        }
        catch (Exception ex)
        {
            // Xom 500 o'rniga ANIQ sabab (ichki xato matni bilan) — muammoni ko'rish uchun.
            var detail = ex.InnerException?.Message ?? ex.Message;
            return StatusCode(500, "Buyurtma yaratishda xatolik: " + detail);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  CHEGIRMA (SKIDKA) QO'LLASH
    //  Kassir kutilayotgan (Pending) buyurtmaga, to'lovdan OLDIN, ayrim
    //  mahsulotlarga chegirma beradi. Har bir qator uchun yangi (dona) narx
    //  yuboriladi. Tizim:
    //    • Asl narxni (OriginalPrice) saqlab qoladi (bir marta — birinchi
    //      chegirmada). Shu sabab chekda "asl → yangi" va foiz ko'rinadi va
    //      qayta chegirma berilganda asl narx yo'qolmaydi.
    //    • Yangi narx 0..OriginalPrice oralig'ida bo'ladi (manfiy/oshib ketmaydi).
    //    • Buyurtmaning umumiy summasini (TotalSum) qayta hisoblaydi.
    //  Faqat Pending buyurtmaga ruxsat — to'langan chekni o'zgartirib bo'lmaydi.
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/discount")]
    public async Task<IActionResult> ApplyDiscount(int id, [FromBody] DiscountRequest req)
    {
        if (req?.Items == null || req.Items.Count == 0)
            return BadRequest("Chegirma qatorlari yuborilmadi.");

        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound("Buyurtma topilmadi.");

        // Idempotentlik (offline chegirma takror yuborilsa — bir marta qo'llanadi)
        var discOpId = (req.ClientOpId ?? "").Trim();
        if (discOpId.Length > 0 && await _db.SyncOperations.AnyAsync(s => s.OperationId == discOpId))
        {
            var dupd = await _db.Orders
                .Include(o => o.Client).Include(o => o.User).Include(o => o.Cashier)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .FirstAsync(o => o.Id == id);
            return Ok(dupd);
        }

        if (order.Status != "Pending")
            return BadRequest("Chegirma faqat to'lanmagan (kutilayotgan) buyurtmaga beriladi.");

        foreach (var d in req.Items)
        {
            var item = order.Items.FirstOrDefault(i => i.Id == d.ItemId);
            if (item == null) continue;

            // Asl narxni bir marta belgilab qo'yamiz (eski buyurtmalarda 0 bo'lishi mumkin).
            if (item.OriginalPrice < 0.5) item.OriginalPrice = item.Price;

            double newPrice = Math.Round(d.NewPrice);
            if (newPrice < 0) newPrice = 0;
            if (newPrice > item.OriginalPrice) newPrice = item.OriginalPrice; // asl narxdan oshmasin
            item.Price = newPrice;
        }

        // Umumiy summani qayta hisoblaymiz (butun so'mda)
        order.TotalSum = Math.Round(order.Items.Sum(i => i.Price * i.Quantity));

        if (discOpId.Length > 0)
        {
            _db.SyncOperations.Add(new SyncOperation
            {
                OperationId = discOpId,
                Type = "Discount",
                EntityId = order.Id,
                Amount = order.TotalSum,
                AppliedAmount = order.TotalSum,
                Note = "Offline chegirma (kassa)",
                ClientCreatedAt = DateTime.UtcNow,
                AppliedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        var updated = await _db.Orders
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstAsync(o => o.Id == order.Id);

        return Ok(updated);
    }

    // ─────────────────────────────────────────────────────────────
    //  Kassir mahsulotni skaner qiladi -> savatga qo'shadi -> sotadi.
    //  Bu yerda sotuvchi (Pending order) ishtirok etmaydi: buyurtma
    //  to'g'ridan-to'g'ri "Paid" holatda yaratiladi, stok darhol kamayadi.
    //  Mijoz kutmaydi — tezkor xizmat.
    // ─────────────────────────────────────────────────────────────
    [HttpPost("quicksell")]
    public async Task<IActionResult> QuickSell([FromBody] QuickSellRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest("Savat bo'sh.");

        // ─────────────────────────────────────────────────────────────
        //  IDEMPOTENTLIK (offline sotuvlar uchun)
        //  Kassir kompyuteri server o'chgan paytda sotgan bo'lsa, sotuv
        //  lokal navbatga YAGONA ClientOpId (GUID) bilan yoziladi. Server
        //  qaytganda navbat yuboriladi. Tarmoq uzilib qayta yuborilsa yoki
        //  ikki marta bosilsa ham — bu OperationId bo'yicha sotuv FAQAT
        //  BIR MARTA yaratiladi. Shu sabab bitta sotuv ikki marta yozilmaydi.
        // ─────────────────────────────────────────────────────────────
        var opId = (req.ClientOpId ?? "").Trim();
        if (opId.Length > 0)
        {
            var prev = await _db.SyncOperations.FirstOrDefaultAsync(s => s.OperationId == opId);
            if (prev != null)
            {
                // Allaqachon qo'llangan — avval yaratilgan buyurtmani qaytaramiz (takror yaratmaymiz).
                var dup = await _db.Orders
                    .Include(o => o.User)
                    .Include(o => o.Cashier)
                    .Include(o => o.Items).ThenInclude(i => i.Product)
                    .FirstOrDefaultAsync(o => o.Id == prev.EntityId);
                if (dup != null) return Ok(dup);
                // Buyurtma topilmasa (kam ehtimol) — pastdagi yangi yaratishga o'tmaymiz,
                // chunki bu allaqachon hisobga olingan. Bo'sh muvaffaqiyat qaytaramiz.
                return Ok(new { duplicate = true, operationId = opId });
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Stok yetarliligini tekshirish va summani serverda qayta hisoblash
            double total = 0;
            foreach (var item in req.Items)
            {
                var product = await _db.Products.FindAsync(item.ProductId);
                if (product == null) return BadRequest($"Mahsulot topilmadi: {item.ProductId}");
                if (item.Quantity <= 0) return BadRequest($"'{product.Name}' miqdori noto'g'ri.");
                // Offline sotuv ALLAQACHON jismonan bo'lib o'tgan — uni rad eta olmaymiz
                // (aks holda kassir pulni olgan-u, sotuv yo'qoladi). Shu sabab offline
                // sotuvda stok yetmasa ham o'tkazamiz (stok manfiyga — "kamomad"ga tushadi,
                // tizim buni allaqachon ⚠️ bilan ko'rsatadi). Onlayn sotuvda esa eski
                // qoida saqlanadi: stok yetmasa rad etiladi.
                if (!req.Offline && product.TotalPieces < item.Quantity)
                    return BadRequest($"'{product.Name}' uchun yetarli stok yo'q. Mavjud: {product.TotalPieces}");
                total += item.Quantity * item.Price;
            }

            var (qsCash, qsCard, qsType) = SplitPayment(total, req.PaymentType, req.CashAmount, req.CardAmount);

            // Offline sotuvda — sotuv aslida bo'lib o'tgan vaqtni saqlaymiz (hisobotlar to'g'ri
            // bo'lishi uchun). Vaqt kelmasa yoki onlayn bo'lsa — hozirgi server vaqti.
            DateTime soldAt = (req.Offline && req.SoldAt.HasValue && req.SoldAt.Value != default)
                ? req.SoldAt.Value.ToUniversalTime()
                : DateTime.UtcNow;

            var order = new Order
            {
                ClientId = null,                 // naqd xaridor
                UserId = null,                   // sotuvchisiz (to'g'ridan-to'g'ri kassada)
                CashierId = req.CashierId,
                TotalSum = total,
                PaidSum = total,                 // tezkor sotuv: to'liq to'langan
                Status = "Paid",
                PaymentType = qsType,
                CashAmount = qsCash,
                CardAmount = qsCard,
                CreatedAt = soldAt,
                Items = req.Items.Select(i => new OrderItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    Price = i.Price,
                    OriginalPrice = i.Price
                }).ToList()
            };

            _db.Orders.Add(order);

            // Stokni darhol kamaytiramiz
            foreach (var item in req.Items)
            {
                var product = await _db.Products.FindAsync(item.ProductId);
                if (product != null) product.TotalPieces -= item.Quantity;
            }

            // Idempotentlik yozuvi (ClientOpId berilgan bo'lsa) — bu sotuv qayta qo'llanmaydi.
            if (opId.Length > 0)
            {
                _db.SyncOperations.Add(new SyncOperation
                {
                    OperationId = opId,
                    Type = "QuickSell",
                    EntityId = order.Id,            // SaveChanges'dan keyin haqiqiy Id bo'ladi
                    Amount = total,
                    AppliedAmount = total,
                    Note = "Offline kassa sotuvi",
                    ClientCreatedAt = soldAt,
                    AppliedAt = DateTime.UtcNow,
                    Device = string.IsNullOrWhiteSpace(req.Device) ? null : req.Device!.Trim()
                });
            }

            await _db.SaveChangesAsync();

            // EntityId'ni haqiqiy buyurtma Id'siga moslaymiz (Add paytida 0 edi).
            if (opId.Length > 0)
            {
                var rec = await _db.SyncOperations.FirstOrDefaultAsync(s => s.OperationId == opId);
                if (rec != null && rec.EntityId != order.Id)
                {
                    rec.EntityId = order.Id;
                    await _db.SaveChangesAsync();
                }
            }

            await tx.CommitAsync();

            var created = await _db.Orders
                .Include(o => o.User)
                .Include(o => o.Cashier)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .FirstAsync(o => o.Id == order.Id);

            return Ok(created);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, "Sotuvda xatolik: " + ex.Message);
        }
    }

    // Kassir tomonidan to'lov qilish
    [HttpPost("{id}/pay")]
    public async Task<IActionResult> Pay(int id, [FromBody] PayRequest req)
    {
        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Client)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();

        // ── IDEMPOTENTLIK (offline to'lov uchun) ──────────────────────
        //  To'lov offline qilingan bo'lsa, server qaytganda YAGONA ClientOpId
        //  bilan yuboriladi. Takror yuborilsa (tarmoq uzilib qayta urindi yoki
        //  ikki marta bosildi) — to'lov IKKI MARTA qo'llanmaydi (qarz/stok
        //  ikkilanmaydi). Avval qo'llangan buyurtma qaytariladi.
        var payOpId = (req.ClientOpId ?? "").Trim();
        if (payOpId.Length > 0 && await _db.SyncOperations.AnyAsync(s => s.OperationId == payOpId))
        {
            var dup = await _db.Orders
                .Include(o => o.Client).Include(o => o.User).Include(o => o.Cashier)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .FirstAsync(o => o.Id == id);
            return Ok(dup);
        }

        if (order.Status != "Pending") return BadRequest("Bu buyurtma allaqachon to'langan.");

        // Stokni kamaytirish
        foreach (var item in order.Items)
        {
            if (item.Product != null)
                item.Product.TotalPieces -= item.Quantity;
        }

        order.PaidSum = req.PaidSum;
        // Naqd/plastik/aralash taqsimotini hisoblab saqlaymiz (statistika aniq bo'lishi uchun).
        var (cashPart, cardPart, normType) = SplitPayment(req.PaidSum, req.PaymentType, req.CashAmount, req.CardAmount);
        order.PaymentType = normType;
        order.CashAmount = cashPart;
        order.CardAmount = cardPart;
        // Qaysi kassir to'lovni qabul qilganini saqlaymiz (Mening sotuvlarim uchun)
        if (req.CashierId.HasValue && req.CashierId.Value > 0)
            order.CashierId = req.CashierId.Value;

        // Pul butun so'm bilan ishlaydi — tiyin qoldiqlari bo'lmasligi uchun yaxlitlaymiz.
        double debt = Math.Round(order.TotalSum - req.PaidSum);
        if (debt > 0.5)
        {
            order.Status = "Debt";
            if (order.ClientId.HasValue && order.Client != null)
                order.Client.DebtBalance = Math.Round(order.Client.DebtBalance + debt);
        }
        else
        {
            order.Status = "Paid";
        }

        // ─────────────────────────────────────────────────────────────
        //  ORTIQCHA PULNI ESKI QARZGA O'TKAZISH
        //  Mijoz joriy buyurtma summasidan ko'proq pul bersa va undan
        //  eski qarz bo'lsa — ortiqcha summa shu qarzni kamaytiradi.
        //  Bu kamaytirish DebtPayment sifatida tarixga yoziladi (admin
        //  panelida qancha, qachon, qaysi chek orqali ekani ko'rinadi).
        // ─────────────────────────────────────────────────────────────
        double debtPay = Math.Round(req.DebtPaymentSum);
        if (debtPay > 0 && order.ClientId.HasValue && order.Client != null)
        {
            double applied = Math.Min(debtPay, order.Client.DebtBalance);
            if (applied > 0)
            {
                order.Client.DebtBalance = Math.Round(order.Client.DebtBalance - applied);
                if (order.Client.DebtBalance < 1) order.Client.DebtBalance = 0;

                // Kassir ismini izohga qo'shamiz (admin tushunarli ko'rsin)
                string cashierName = "";
                if (order.CashierId.HasValue)
                {
                    var cashier = await _db.Users.FindAsync(order.CashierId.Value);
                    cashierName = cashier?.FullName ?? "";
                }

                _db.DebtPayments.Add(new DebtPayment
                {
                    ClientId = order.ClientId.Value,
                    Amount = applied,
                    RemainingAfter = order.Client.DebtBalance,
                    PaidAt = DateTime.UtcNow,
                    Note = $"Kassada to'landi (chek #{order.Id}" +
                           (string.IsNullOrEmpty(cashierName) ? ")" : $", kassir: {cashierName})")
                });
            }
        }

        // Idempotentlik yozuvi — bu to'lov boshqa qayta qo'llanmaydi.
        if (payOpId.Length > 0)
        {
            _db.SyncOperations.Add(new SyncOperation
            {
                OperationId = payOpId,
                Type = "Pay",
                EntityId = order.Id,
                Amount = req.PaidSum,
                AppliedAmount = req.PaidSum,
                Note = "Offline to'lov (kassa)",
                ClientCreatedAt = req.OccurredAt ?? DateTime.UtcNow,
                AppliedAt = DateTime.UtcNow,
                Device = string.IsNullOrWhiteSpace(req.Device) ? null : req.Device!.Trim()
            });
        }

        await _db.SaveChangesAsync();

        // Kassir ma'lumoti bilan qayta yuklab qaytaramiz
        var paid = await _db.Orders
            .Include(o => o.Client)
            .Include(o => o.User)
            .Include(o => o.Cashier)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstAsync(o => o.Id == order.Id);
        return Ok(paid);
    }

    // Buyurtmani bekor qilish
    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        if (order == null) return NotFound();
        if (order.Status != "Pending") return BadRequest("Faqat kutilayotgan buyurtmalarni bekor qilish mumkin.");
        _db.Orders.Remove(order);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ─────────────────────────────────────────────────────────────
    //  TO'LOV TAQSIMOTI (naqd / plastik / aralash)
    //  orderPortion — buyurtmaga ketgan summa. Natija: (naqd, plastik, tur).
    //  cash + card HAR DOIM orderPortion ga teng bo'ladi.
    // ─────────────────────────────────────────────────────────────
    private static (double cash, double card, string type) SplitPayment(
        double orderPortion, string? paymentType, double cashReq, double cardReq)
    {
        orderPortion = Math.Max(0, Math.Round(orderPortion));
        string t = (paymentType ?? "Cash").Trim();

        if (t.Equals("Mixed", StringComparison.OrdinalIgnoreCase))
        {
            double card = Math.Round(Math.Max(0, cardReq));
            if (card > orderPortion) card = orderPortion;   // buyurtma ulushidan oshmasin
            double cash = orderPortion - card;              // qolgani naqd
            if (cash <= 0.5 && card > 0.5) return (0, card, "Card");
            if (card <= 0.5 && cash > 0.5) return (cash, 0, "Cash");
            return (cash, card, "Mixed");
        }
        if (t.Equals("Card", StringComparison.OrdinalIgnoreCase))
            return (0, orderPortion, "Card");
        return (orderPortion, 0, "Cash");
    }

    // Statistika uchun: buyurtmaning naqd qismi (eski buyurtmalarda PaymentType bo'yicha).
    private static double CashPart(Order o)
    {
        if (o.CashAmount > 0.5 || o.CardAmount > 0.5) return o.CashAmount; // yangi (taqsimotli)
        return o.PaymentType == "Card" ? 0 : o.PaidSum;                    // eski buyurtma
    }

    // Statistika uchun: buyurtmaning plastik qismi.
    private static double CardPart(Order o)
    {
        if (o.CashAmount > 0.5 || o.CardAmount > 0.5) return o.CardAmount;
        return o.PaymentType == "Card" ? o.PaidSum : 0;
    }
}

public class OrderRequest
{
    public int? ClientId { get; set; }
    public int? UserId { get; set; }
    public double TotalSum { get; set; }
    public List<OrderItemRequest> Items { get; set; } = new();
}

public class OrderItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public double Price { get; set; }
}

public class PayRequest
{
    public double PaidSum { get; set; }
    public string PaymentType { get; set; } = "Cash"; // Cash | Card | Mixed
    public int? CashierId { get; set; }                // To'lovni qabul qilgan kassir
    public double DebtPaymentSum { get; set; }         // Ortiqcha puldan eski qarzga o'tkaziladigan summa
    public double CashAmount { get; set; }             // Aralash to'lovda buyurtmaga ketgan naqd qism
    public double CardAmount { get; set; }             // Aralash to'lovda buyurtmaga ketgan plastik qism

    // ── OFFLINE/idempotentlik maydonlari (ixtiyoriy) ──────────────
    public string? ClientOpId { get; set; }            // YAGONA op id — takror yuborilsa bir marta qo'llanadi
    public DateTime? OccurredAt { get; set; }          // to'lov aslida (offline) bo'lib o'tgan vaqt
    public string? Device { get; set; }                // qaysi kassa kompyuteri (audit)
}

public class QuickSellRequest
{
    public int? CashierId { get; set; }
    public string PaymentType { get; set; } = "Cash"; // Cash | Card | Mixed
    public double CashAmount { get; set; }             // Aralash to'lovda naqd qism
    public double CardAmount { get; set; }             // Aralash to'lovda plastik qism
    public List<OrderItemRequest> Items { get; set; } = new();

    // ── OFFLINE SOTUV maydonlari (ixtiyoriy) ──────────────────────
    // ClientOpId: kassir kompyuteri offline sotgan sotuvning YAGONA identifikatori
    //   (GUID). Server qaytganda yuboriladi; takror yuborilsa ham bir marta qo'llanadi.
    // SoldAt: sotuv aslida (offline) bo'lib o'tgan vaqt — hisobotlar to'g'ri bo'lishi uchun.
    // Offline: true bo'lsa, stok yetmasa ham sotuv o'tkaziladi (sotuv allaqachon bo'lgan).
    // Device: qaysi kassa kompyuteridan kelgani (audit uchun, ixtiyoriy).
    public string? ClientOpId { get; set; }
    public DateTime? SoldAt { get; set; }
    public bool Offline { get; set; }
    public string? Device { get; set; }
}

// Chegirma so'rovi — har bir qator uchun yangi (dona) narx.
public class DiscountRequest
{
    public List<DiscountItem> Items { get; set; } = new();

    // Offline/idempotentlik (ixtiyoriy) — takror yuborilsa bir marta qo'llanadi.
    public string? ClientOpId { get; set; }
}

public class DiscountItem
{
    public int ItemId { get; set; }      // OrderItem.Id
    public double NewPrice { get; set; } // yangi (dona) narx
}