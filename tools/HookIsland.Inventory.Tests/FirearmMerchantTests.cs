using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.InventoryTests;

internal static partial class Program
{
    private static IDeterministicSimulation<HookState> FirearmInventoryGame(int wood=0)
    {
        var game=new HookGame().Create();var initial=IslandMap.CreatePlatform();
        var map=new IslandMap(initial.Shore,initial.Pier,initial.Spawns,initial.FishSpots,initial.Platform,new FishingConfig(firearms:new FirearmConfig()));
        var session=new SessionDescriptor(SessionId,1,HookGame.Rate,Enumerable.Range(0,2).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray());
        game.Initialize(new SimulationInitialization(session,new ByteString(map.Encode())));
        var s=game.Capture();var p=s.Players.ToArray();for(int n=0;n<p.Length;n++){p[n].X=n*500;p[n].Z=0;p[n].Coins=p[n].CoinsEarned=5000;}
        var inventory=new InventorySystem(s.Tackle!.Inventory);if(wood>0)PersonalMaterialInventory.Credit(ref p[0],TileKind.Wood,wood,inventory);
        game.Restore(new HookState(s.Steps,p,s.Fish,10000,s.Tiles,s.Driftwood,new TackleState(s.Tackle.Config,inventory.Capture(),s.Tackle.Merchants),s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale));return game;
    }
    private static IEnumerable<(string,Action)> FirearmMerchantCases()
    {
        yield return ("firearm_merchant_contested_guns_sell_once_and_ammunition_is_unavailable",()=>{
            var game=FirearmInventoryGame();var initial=game.Capture();
            Require(initial.Tackle!.Merchants[0].FirearmStock.SequenceEqual(FirearmCatalog.All.Select(g=>g.Stock)) && initial.Tackle.Merchants[0].AmmunitionStock.SequenceEqual(new int[AmmunitionCatalog.All.Count]),"Wrong per-batch ranged stock.");
            GameStep(game,Command(InventoryAction.Buy,FirearmCatalog.OfferOffset,1,batch:1),Command(InventoryAction.Buy,FirearmCatalog.OfferOffset,1,batch:1));
            GameStep(game,Command(InventoryAction.Buy,FirearmCatalog.OfferOffset,2,batch:1),Command(InventoryAction.Buy,FirearmCatalog.OfferOffset,2,batch:1));
            var s=game.Capture();Require(s.Players.All(p=>p.LastInventoryResult==InventoryResult.SoldOut && p.Coins==4700),"Gun stock race charged rejected purchase.");
            for(uint request=3;request<=5;request++)GameStep(game,Command(InventoryAction.Buy,AmmunitionCatalog.OfferOffset,request,batch:1),Command(InventoryAction.Buy,AmmunitionCatalog.OfferOffset,request,batch:1));
            GameStep(game,Command(InventoryAction.Buy,AmmunitionCatalog.OfferOffset,6,batch:1),Command(InventoryAction.Buy,AmmunitionCatalog.OfferOffset,6,batch:1));s=game.Capture();
            Require(s.Players.All(p=>p.LastInventoryResult==InventoryResult.Disabled && p.Coins==4700) && s.Tackle!.Inventory.PurchasedAmmunition[0]==0,"Unavailable ammunition purchase charged coins.");
            Require(!s.Tackle!.Inventory.Items.Any(i=>i.Kind==ItemKind.Ammunition),"Unavailable packs awarded rounds.");
            var codec=new HookGame();var saved=codec.Encode(s);game.Restore(codec.Decode(saved));
            GameStep(game,Command(InventoryAction.Buy,AmmunitionCatalog.OfferOffset,6,batch:1));
            Require(game.Capture().Players[0].Coins==4700 && game.Capture().Tackle!.Inventory.PurchasedAmmunition[0]==0,"Duplicate purchase replayed after restore.");
            s=game.Capture();var merchant=s.Tackle!.Merchants[0];
            game.Restore(new HookState(merchant.NextRestock-1,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale));GameStep(game);s=game.Capture();
            Require(s.Tackle!.Merchants[0].FirearmStock[0]==2 && s.Tackle.Merchants[0].AmmunitionStock[0]==0,"Restock reintroduced ammunition.");
            codec.Decode(codec.Encode(s));
            GameStep(game,Command(InventoryAction.Buy,FirearmCatalog.OfferOffset,7,batch:1));
            Require(game.Capture().Players[0].LastInventoryResult==InventoryResult.StaleStock && game.Capture().Players[0].Coins==4700,"Old batch purchased a new gun.");
        });
        yield return ("firearm_merchant_legacy_ammunition_stock_cannot_be_bought",()=>{
            var game=FirearmInventoryGame();var s=game.Capture();var shelves=s.Tackle!.Merchants.ToArray();var shelf=shelves[0];
            shelves[0]=new MerchantState(shelf.Id,shelf.Active,shelf.ActivatedAt,shelf.Batch,shelf.NextRestock,shelf.Stock,shelf.FoodStock,shelf.FirearmStock,AmmunitionCatalog.All.Select((_,i)=>i==0?6:0));
            game.Restore(new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,new TackleState(s.Tackle.Config,s.Tackle.Inventory,shelves),s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale));
            var buy=Command(InventoryAction.Buy,AmmunitionCatalog.OfferOffset,1,batch:1);GameStep(game,buy);
            s=game.Capture();Require(s.Players[0].LastInventoryResult==InventoryResult.Disabled && s.Players[0].Coins==5000 && s.Tackle!.Merchants[0].AmmunitionStock[0]==6,"Legacy shelf accepted an obsolete ammunition purchase.");
            var codec=new HookGame();game.Restore(codec.Decode(codec.Encode(s)));GameStep(game,buy);
            GameStep(game,Command(InventoryAction.Buy,AmmunitionCatalog.OfferOffset,2,batch:1));s=game.Capture();
            Require(s.Players[0].LastInventoryResult==InventoryResult.Disabled && s.Tackle!.Inventory.PurchasedAmmunition[0]==0 && s.Players[0].Coins==5000,"Restore or fresh request re-enabled old ammunition sales.");
        });
        yield return ("firearm_merchant_disabled_offers_are_rejected_and_old_offer_ids_stay_valid",()=>{
            var game=Game();GameStep(game,Command(InventoryAction.Buy,FirearmCatalog.OfferOffset,1,batch:1));
            Require(game.Capture().Players[0].LastInventoryResult==InventoryResult.Disabled && game.Capture().Tackle!.Merchants[0].FirearmStock[0]==0,"Disabled game exposed firearm stock.");
            foreach(int offer in new[]{0,7,8,9,10}.Concat(Enumerable.Range(FirearmCatalog.OfferOffset,FirearmCatalog.All.Count)).Concat(Enumerable.Range(AmmunitionCatalog.OfferOffset,AmmunitionCatalog.All.Count)))new InventoryCommand(InventoryAction.Buy,offer,batch:1,requestId:1).Encode();
            foreach(int offer in new[]{11,15,FirearmCatalog.OfferOffset+FirearmCatalog.All.Count,31,AmmunitionCatalog.OfferOffset+AmmunitionCatalog.All.Count})
            {
                bool rejected=false;try{new InventoryCommand(InventoryAction.Buy,offer,batch:1,requestId:1).Encode();}catch(System.IO.InvalidDataException){rejected=true;}
                Require(rejected,"Undefined merchant offer accepted.");
            }
        });
        yield return ("long_gun_merchant_stock_is_shared_and_losing_sniper_purchase_is_atomic",()=>{
            for(int type=1;type<4;type++)
            {
                var game=FirearmInventoryGame();var gun=FirearmCatalog.Get(type);
                var buy=Command(InventoryAction.BuyAndEquip,FirearmCatalog.OfferOffset+type,1,batch:1);
                GameStep(game,buy,buy);var s=game.Capture();int winners=Math.Min(2,gun.Stock);
                Require(s.Tackle!.Merchants[0].FirearmStock[type]==gun.Stock-winners,"Stock was oversold");
                for(int slot=0;slot<2;slot++)
                {
                    bool won=slot<winners;Require(s.Players[slot].Coins==5000-(won?gun.Price:0),"Rejected purchase changed wallet");
                    Require(s.Players[slot].LastInventoryResult==(won?InventoryResult.Success:InventoryResult.SoldOut),"Wrong shared-stock winner");
                }
                new HookGame().Decode(new HookGame().Encode(s));
            }
        });
    }
}
