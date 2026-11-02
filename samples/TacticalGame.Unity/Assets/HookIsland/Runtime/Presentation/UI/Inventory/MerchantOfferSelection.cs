using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>The selected offer is bound to one merchant and one confirmed batch.</summary>
    public sealed class MerchantOfferSelection
    {
        public MerchantState Shelf {get;private set;}
        public int Offer {get;private set;}=-1;
        public void Bind(MerchantState shelf)
        {
            if(shelf==null || Shelf==null || shelf.Id!=Shelf.Id || shelf.Batch!=Shelf.Batch || !Exists(shelf,Offer))Offer=-1;
            Shelf=shelf;
        }
        public void Select(int offer){Offer=Shelf!=null && Exists(Shelf,offer)?offer:-1;}
        public InventoryCommand Command(bool equip,bool dropOld=false)=>Shelf==null || Offer<0 || (Offer>=FoodCatalog.OfferOffset?Shelf.FoodStock[Offer-FoodCatalog.OfferOffset]==0:Shelf.Stock[Offer].Sold)?default:
            new InventoryCommand(equip && Offer<FoodCatalog.OfferOffset?InventoryAction.BuyAndEquip:InventoryAction.Buy,Offer,batch:Shelf.Batch,dropOld:equip && Offer<FoodCatalog.OfferOffset && dropOld,merchantId:Shelf.Id);
        private static bool Exists(MerchantState shelf,int index)=>index>=0 && (index<shelf.Stock.Count || index>=FoodCatalog.OfferOffset && index<FoodCatalog.OfferOffset+shelf.FoodStock.Count);
    }
}
