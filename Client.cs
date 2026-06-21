namespace StoreSystem.Api.Models;

// Mijoz (xaridor). DebtBalance = mijozning BIZGA qarzi (nasiyaga olgan mahsulotlar).
public class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public double DebtBalance { get; set; } = 0;   // mijoz bizga qancha qarzdor

    // ── ILOVAGACHA BO'LGAN (boshlang'ich) QARZ ESLATMASI ──────────────
    // Mijoz yangi tizimga qo'shilganda, undan oldin (ilova o'rnatilmasidan
    // avval) yig'ilib qolgan qarz bo'lishi mumkin. Admin/Buxgalter shu qarzni
    // mijoz qo'shish oynasidagi "switch" orqali kiritadi (firma kabi).
    //  • DebtDueDate      — shu qarzni eslatish (undirish) sanasi (ixtiyoriy).
    //  • DebtReminderDone — eslatma ko'rib chiqilganmi (banner qaytmasligi uchun).
    public DateTime? DebtDueDate { get; set; }
    public bool DebtReminderDone { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
