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
            if(shelf==null || Shelf==null || shelf.Id!=Shelf.Id || shelf.Batch!=Shelf.Batch || Offer>=shelf.Stock.Count)Offer=-1;
            Shelf=shelf;
        }
        public void Select(int offer){Offer=Shelf!=null && offer>=0 && offer<Shelf.Stock.Count?offer:-1;}
        public InventoryCommand Command(bool equip,bool dropOld=false)=>Shelf==null || Offer<0 || Shelf.Stock[Offer].Sold?default:
            new InventoryCommand(equip?InventoryAction.BuyAndEquip:InventoryAction.Buy,Offer,batch:Shelf.Batch,dropOld:equip && dropOld,merchantId:Shelf.Id);
    }
}
