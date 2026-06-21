namespace StoreSystem.Api.Models;

// ═══════════════════════════════════════════════════════════════════
//  VOZVRAT (QAYTARISH)
//  Mijoz (yoki naqd xaridor) ilgari olgan mahsulotni qaytarib bersa,
//  kassir "Mening sotuvlarim" bo'limidan asl chekni topadi va shu chek
//  bo'yicha qaytarishni rasmiylashtiradi. Natijada:
//    • Qaytarilgan mahsulot OMBORGA qaytadi (Product.TotalPieces oshadi).
//    • Pul mijozga qaytariladi (naqd/plastik) yoki — agar chek qarzga
//      bo'lgan bo'lsa — mijozning qarzidan ayriladi (DebtReduced).
//    • Asl buyurtma (Order) O'ZGARMAYDI (audit uchun): faqat uning
//      ReturnedSum maydoni oshadi. Shu sabab qisman va ko'p martalik
//      qaytarishlar to'g'ri hisoblanadi (bir mahsulotni 2 marta
//      qaytarib bo'lmaydi).
//
//  TotalSum  — qaytarilgan jami summa (har doim musbat).
//  CashRefund + CardRefund + DebtReduced = TotalSum.
// ═══════════════════════════════════════════════════════════════════
public class Return
{
    public int Id { get; set; }

    public int OrderId { get; set; }            // qaysi chekdan qaytarildi
    public Order? Order { get; set; }

    public int? ClientId { get; set; }          // chek mijozga tegishli bo'lsa
    public Client? Client { get; set; }

    public int? CashierId { get; set; }         // qaytarishni qabul qilgan kassir
    public User? Cashier { get; set; }

    public double TotalSum { get; set; }        // qaytarilgan jami summa (musbat)
    public double CashRefund { get; set; }      // naqd qaytarilgan qism
    public double CardRefund { get; set; }      // plastik qaytarilgan qism
    public double DebtReduced { get; set; }     // mijoz qarzidan ayrilgan qism
    public string RefundType { get; set; } = "Cash"; // Cash | Card | Debt | Mixed

    public string? Reason { get; set; }         // qaytarish sababi (ixtiyoriy)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ReturnItem> Items { get; set; } = new();
}

// Qaytarishdagi bitta mahsulot qatori.
// OrderItemId — asl buyurtmaning qaysi qatoridan qaytarilgani (bir buyurtmada
// bir xil mahsulot ikki qatorda bo'lsa ham aniq hisoblash uchun).
public class ReturnItem
{
    public int Id { get; set; }

    public int ReturnId { get; set; }
    public Return? Return { get; set; }

    public int OrderItemId { get; set; }        // asl buyurtma qatori
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; }           // qaytarilgan dona/birlik soni
    public double Price { get; set; }           // chekdagi sotuv narxi (dona)
}