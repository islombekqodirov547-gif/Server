namespace StoreSystem.Api.Models;

// Yetkazib beruvchi (firma) - mahsulot olinadigan tashkilot.
// DebtBalance = bizning shu firmaga qarzimiz (nasiyaga olingan mahsulotlar).
public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public double DebtBalance { get; set; } = 0;   // biz firmaga qancha qarzdormiz

    // ── ILOVAGACHA BO'LGAN (boshlang'ich) QARZ ESLATMASI ──────────────
    // Firma yangi tizimga qo'shilganda, undan oldin (ilova o'rnatilmasidan
    // avval) yig'ilib qolgan qarz bo'lishi mumkin. Buxgalter shu qarzni
    // firma qo'shish oynasidagi "switch" orqali kiritadi.
    //  • DebtDueDate     — shu qarzni eslatish (to'lash) sanasi (ixtiyoriy).
    //  • DebtReminderDone — eslatma ko'rib chiqilganmi (banner qaytmasligi uchun).
    public DateTime? DebtDueDate { get; set; }
    public bool DebtReminderDone { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
