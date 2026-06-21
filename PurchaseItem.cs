using System.ComponentModel.DataAnnotations.Schema;

namespace StoreSystem.Api.Models;

// Kirimdagi bitta mahsulot qatori.
// Quantity = necha blok/birlik olindi, PiecesPerBlock = o'sha paytdagi blokdagi dona soni,
// UnitCost = 1 blok/birlik xarid narxi.
// Omborga qo'shiladigan dona = Quantity * PiecesPerBlock.
public class PurchaseItem
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; }          // necha blok/birlik
    public int PiecesPerBlock { get; set; } = 1; // snapshot (o'sha paytdagi)
    public double UnitCost { get; set; }        // 1 blok/birlik xarid narxi

    [NotMapped]
    public int PiecesAdded => Quantity * (PiecesPerBlock <= 0 ? 1 : PiecesPerBlock);
    [NotMapped]
    public double LineTotal => Quantity * UnitCost;
}
