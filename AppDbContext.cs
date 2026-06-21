using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products { get; set; }
    public DbSet<ProductBarcode> ProductBarcodes { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<DebtPayment> DebtPayments { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    // Vozvrat (qaytarish) moduli
    public DbSet<Return> Returns { get; set; }
    public DbSet<ReturnItem> ReturnItems { get; set; }
    public DbSet<Stock> Stocks { get; set; }
    public DbSet<User> Users { get; set; }
    // Oluv (kirim) moduli
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<Purchase> Purchases { get; set; }
    public DbSet<PurchaseItem> PurchaseItems { get; set; }
    public DbSet<SupplierPayment> SupplierPayments { get; set; }
    // Sinxron (offline -> server) operatsiyalari tarixi (idempotentlik uchun)
    public DbSet<SyncOperation> SyncOperations { get; set; }
    // Umumiy sozlamalar (kalit/qiymat) — masalan buxgalter o'chirish paroli
    public DbSet<Setting> Settings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Mahsulot -> Barcodes (bir mahsulotga ko'p shtrix-kod).
        // Mahsulot o'chsa — uning barcha kodlari ham o'chadi (cascade).
        // Har bir kod butun bazada yagona bo'lishi kerak (unique).
        modelBuilder.Entity<ProductBarcode>()
            .HasOne(b => b.Product)
            .WithMany(p => p.Barcodes)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProductBarcode>()
            .HasIndex(b => b.Code)
            .IsUnique();

        // Mijoz -> DebtPayments (qarz to'lovlari tarixi).
        // Mijoz o'chsa — uning to'lov tarixi ham o'chadi (cascade).
        modelBuilder.Entity<DebtPayment>()
            .HasOne(p => p.Client)
            .WithMany()
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Order -> User (sotuvchi) va Order -> Cashier (kassir) - ikkalasi ham Users jadvaliga
        // bog'lanadi. SQL Server'da bir nechta cascade yo'l xatosi chiqmasligi uchun
        // o'chirishni Restrict qilib qo'yamiz (xodim o'chsa buyurtmalar o'chmaydi).
        modelBuilder.Entity<Order>()
            .HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Order>()
            .HasOne(o => o.Cashier)
            .WithMany()
            .HasForeignKey(o => o.CashierId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── VOZVRAT (QAYTARISH) bog'lanishlari ──────────────────────
        // Qaytarish -> asl chek (Order). Chek o'chmaydi (Restrict) — audit saqlanadi.
        modelBuilder.Entity<Return>()
            .HasOne(r => r.Order)
            .WithMany()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Qaytarish -> mijoz (ixtiyoriy). Mijoz o'chsa qaytarish tarixi o'chmaydi.
        modelBuilder.Entity<Return>()
            .HasOne(r => r.Client)
            .WithMany()
            .HasForeignKey(r => r.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        // Qaytarish -> kassir (Users). Xodim o'chsa qaytarish o'chmaydi (Restrict).
        modelBuilder.Entity<Return>()
            .HasOne(r => r.Cashier)
            .WithMany()
            .HasForeignKey(r => r.CashierId)
            .OnDelete(DeleteBehavior.Restrict);

        // Qaytarish -> qatorlar. Qaytarish o'chsa, qatorlari ham o'chadi (Cascade).
        modelBuilder.Entity<ReturnItem>()
            .HasOne(i => i.Return)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.ReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        // Qaytarish qatori -> mahsulot. Mahsulot o'chsa qaytarish qatori o'chmaydi (Restrict).
        modelBuilder.Entity<ReturnItem>()
            .HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Default admin user seed
        modelBuilder.Entity<User>().HasData(new User
        {
            Id = 1,
            FullName = "Administrator",
            Username = "admin",
            Password = "admin123",
            Role = "Admin",
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1)
        });

        // ── OLUV (KIRIM) MODULI bog'lanishlari ──────────────────────
        // Kirim -> Firma. Firma o'chsa kirimlar o'chmaydi (Restrict).
        modelBuilder.Entity<Purchase>()
            .HasOne(p => p.Supplier)
            .WithMany()
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        // Kirim -> qatorlar. Kirim o'chsa, qatorlari ham o'chadi (Cascade).
        modelBuilder.Entity<PurchaseItem>()
            .HasOne(i => i.Purchase)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.PurchaseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Kirim qatori -> mahsulot. Mahsulot o'chsa kirim qatori o'chmaydi (Restrict).
        modelBuilder.Entity<PurchaseItem>()
            .HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Firmaga to'lov -> Firma. Firma o'chsa to'lov tarixi ham o'chadi (Cascade).
        modelBuilder.Entity<SupplierPayment>()
            .HasOne(p => p.Supplier)
            .WithMany()
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── SINXRON ──────────────────────────────────────────────────
        // Har bir offline amalning OperationId'si butun bazada YAGONA bo'lishi
        // kerak. Shu indeks tufayli bir amal ikki marta yozilib qolmaydi.
        modelBuilder.Entity<SyncOperation>()
            .HasIndex(o => o.OperationId)
            .IsUnique();

        // Sozlama kaliti (Key) butun bazada yagona bo'lishi kerak (upsert uchun).
        modelBuilder.Entity<Setting>()
            .HasIndex(s => s.Key)
            .IsUnique();
    }
}