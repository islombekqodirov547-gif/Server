namespace StoreSystem.Api.Models;

// Firmaga qilingan to'lov (nasiya/qarzni kamaytirish) tarixi.
// Mijoz DebtPayment'iga o'xshash, lekin bu biz firmaga to'laymiz.
public class SupplierPayment
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public double Amount { get; set; }          // shu to'lovda berilgan summa
    public double RemainingAfter { get; set; }  // to'lovdan keyin qolgan qarz
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
}
