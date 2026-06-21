namespace StoreSystem.Api.Models;

// Oddiy "kalit/qiymat" sozlama. Tizimning umumiy (server) sozlamalarini
// saqlash uchun ishlatiladi. Hozircha asosiy foydalanish:
//   Key = "DeletePassword"  → Buxgalter panelida mahsulot/mijoz/firma
//   o'chirilganda so'raladigan parol. Admin uni panelidan o'rnatadi yoki
//   o'zgartiradi. Bo'sh bo'lsa — parol so'ralmaydi (admin o'chirib qo'ygan).
public class Setting
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
