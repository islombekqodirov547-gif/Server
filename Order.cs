namespace StoreSystem.Api.Models;

public class Order
{
    public int Id { get; set; }
    public int? ClientId { get; set; }
    public Client? Client { get; set; }
    public int? UserId { get; set; }           // Qaysi sotuvchidan kelgan
    public User? User { get; set; }
    public int? CashierId { get; set; }         // Qaysi kassir to'lovni qabul qilgan
    public User? Cashier { get; set; }
    public double TotalSum { get; set; }
    public double PaidSum { get; set; }
    public string Status { get; set; } = "Pending"; // Pending | Paid | Debt
    public string PaymentType { get; set; } = "Cash"; // Cash | Card | Mixed
    // Aralash to'lovda (Mixed) naqd va plastik qismlari alohida saqlanadi.
    // Toza naqd/plastik to'lovda ham to'ldiriladi (CashAmount+CardAmount = PaidSum).
    public double CashAmount { get; set; }
    public double CardAmount { get; set; }
    // Shu chekdan jami qaytarilgan (vozvrat qilingan) summa. Qaytarish bo'lmasa 0.
    // Asl buyurtma o'zgarmaydi — qaytarishlar shu maydonni oshiradi (qisman va
    // ko'p martalik qaytarishlarni to'g'ri kuzatish uchun).
    public double ReturnedSum { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public double Price { get; set; }
    // Chegirmagacha bo'lgan asl (birlik/dona) narx. Chegirma berilganda Price
    // kamayadi, OriginalPrice esa asl narxni saqlaydi (chekda "asl narx → yangi
    // narx" va chegirma foizini ko'rsatish uchun). Chegirma bo'lmasa Price ga teng.
    public double OriginalPrice { get; set; }
}