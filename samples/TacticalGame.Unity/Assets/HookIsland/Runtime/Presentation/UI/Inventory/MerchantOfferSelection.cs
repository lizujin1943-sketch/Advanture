using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>One merchant/batch-bound offer; special commodities have independent stock counters.</summary>
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
        public bool TryDescribe(out ItemKind kind,out int type,out int stock)
        {
            kind=default;type=stock=0;
            if(Shelf==null || !Exists(Shelf,Offer))return false;
            if(Offer>=FirearmCatalog.OfferOffset){kind=ItemKind.Firearm;type=Offer-FirearmCatalog.OfferOffset;stock=Shelf.FirearmStock[type];}
            else if(Offer>=FoodCatalog.OfferOffset){kind=ItemKind.Food;type=Offer-FoodCatalog.OfferOffset;stock=Shelf.FoodStock[type];}
            else {var item=Shelf.Stock[Offer];kind=item.Kind;type=item.Type;stock=item.Sold?0:1;}
            return true;
        }
        public InventoryCommand Command(bool equip,bool dropOld=false)
        {
            if(!TryDescribe(out var kind,out var type,out var stock) || stock==0)return default;
            bool equipment=equip && InventoryCatalog.Get(kind,type).Slot!=EquipmentSlot.None;
            return new InventoryCommand(equipment?InventoryAction.BuyAndEquip:InventoryAction.Buy,Offer,batch:Shelf.Batch,dropOld:equipment && dropOld,merchantId:Shelf.Id);
        }
        private static bool Exists(MerchantState shelf,int index)=>index>=0 && (index<shelf.Stock.Count ||
            index>=FoodCatalog.OfferOffset && index<FoodCatalog.OfferOffset+shelf.FoodStock.Count ||
            index>=FirearmCatalog.OfferOffset && index<FirearmCatalog.OfferOffset+shelf.FirearmStock.Count);
    }
}
