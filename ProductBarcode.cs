namespace StoreSystem.Api.Models;

// Bitta mahsulotga bir nechta shtrix-kod (barcode) biriktirish uchun.
// Masalan: "Yogurt 200gr" mahsulotining qulupnay, banan, shaftoli turlari —
// hammasi bir narx, bir o'lcham, ammo har birining o'z shtrix-kodi bo'lishi mumkin.
// Shu turlardan biri skaner qilinsa ham — ayni bitta mahsulot topiladi.
public class ProductBarcode
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string Code { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}