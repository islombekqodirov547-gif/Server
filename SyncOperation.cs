namespace StoreSystem.Api.Models;

// ═══════════════════════════════════════════════════════════════════
//  SINXRON OPERATSIYASI (idempotentlik kafolati)
//  ───────────────────────────────────────────────────────────────────
//  Boshliq ko'chada (internetsiz) qarz yig'ganda yoki firmaga to'laganda,
//  Android ilova har bir amalga YAGONA OperationId (GUID) beradi va uni
//  navbatga (queue) qo'yadi. Do'konga qaytib "Sinxron" bosilganda bu
//  amallar serverga yuboriladi.
//
//  Server har bir OperationId'ni shu jadvalga yozib boradi. Agar bir amal
//  ikki marta yuborilsa (tarmoq uzilib qayta urindi, yoki boshliq ikki
//  marta bosdi) — server uni TAKROR QO'LLAMAYDI. Shu sabab qarz hech
//  qachon ikki barobar kamayib ketmaydi. Bu — sinxronning eng muhim
//  xavfsizlik kafolati.
// ═══════════════════════════════════════════════════════════════════
public class SyncOperation
{
    public int Id { get; set; }

    // Android ilova bergan yagona identifikator (GUID). Butun bazada unique.
    public string OperationId { get; set; } = "";

    // Amal turi: "ClientPayment" (mijoz qarzini to'ladi) | "SupplierPayment" (firmaga to'ladik)
    public string Type { get; set; } = "";

    // Qaysi mijoz/firma (ClientId yoki SupplierId)
    public int EntityId { get; set; }

    // Boshliq kiritgan summa (so'rovdagi)
    public double Amount { get; set; }

    // Serverda haqiqatda qo'llangan summa (qarzdan ortiq bo'lsa cheklanadi)
    public double AppliedAmount { get; set; }

    public string? Note { get; set; }

    // Amal Android'da (offline) bajarilgan vaqt
    public DateTime ClientCreatedAt { get; set; }

    // Server qabul qilib qo'llagan vaqt
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    // Qaysi qurilmadan kelgani (audit uchun, ixtiyoriy)
    public string? Device { get; set; }
}