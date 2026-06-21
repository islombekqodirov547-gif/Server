namespace StoreSystem.Api.Models;

// Kirim (xarid) - bitta firmadan bir martada kelgan mahsulotlar to'plami.
// TotalSum = jami xarid summasi, PaidSum = darhol to'langani.
// Qarz (TotalSum - PaidSum) firmaning DebtBalance'iga qo'shiladi.
public class Purchase
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public double TotalSum { get; set; }
    public double PaidSum { get; set; }
    public string Status { get; set; } = "Paid";   // Paid | Partial | Debt
    public DateTime? DueDate { get; set; }          // nasiya: pul berish belgilangan sana
    // Eslatma ko'rsatildi va buxgalter/admin tomonidan ko'rib chiqildimi (banner
    // qayta-qayta chiqavermasligi uchun). To'lov yoki "Tushunarli" bosilganda true bo'ladi.
    public bool ReminderDone { get; set; } = false;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<PurchaseItem> Items { get; set; } = new();
}
