namespace StoreSystem.Api.Models;

// Mijozning qarzini (qisman yoki to'liq) to'lashi tarixi.
// Masalan: 5 000 000 qarzdan 2 000 000 to'lansa — shu yozuv yaratiladi:
//   Amount = 2 000 000 (bu safar to'langan)
//   RemainingAfter = 3 000 000 (to'lovdan keyin qolgan qarz)
//   PaidAt = to'langan sana/vaqt
public class DebtPayment
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }
    public double Amount { get; set; }          // shu to'lovda berilgan summa
    public double RemainingAfter { get; set; }  // to'lovdan keyin qolgan qarz
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
}