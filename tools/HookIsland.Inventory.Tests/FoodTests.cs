using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.InventoryTests;

internal static partial class Program
{
    private static IEnumerable<(string,Action)> FoodCases()
    {
        yield return ("food_purchase_stacks_five_and_charges_only_buyer",()=>{
            var inventory=new InventorySystem(2);int coins=2000;var food=(ItemKind)8;
            foreach(var pair in new[]{(0,100),(1,200),(2,500)})
            {
                int before=coins;
                Require(inventory.Purchase(1,food,pair.Item1,false,false,Ground,ref coins)==InventoryResult.Success,"Food purchase rejected.");
                Require(coins==before-pair.Item2,"Wrong food price.");
            }
            for(int n=0;n<5;n++)Require(inventory.Purchase(1,food,0,false,false,Ground,ref coins)==InventoryResult.Success,"Repeated food purchase failed.");
            Require(inventory.Capture().Items.Where(i=>i.Kind==food && i.Type==0).Select(i=>i.Count).SequenceEqual(new[]{5,1}),"Food did not fill five-unit stacks.");
            Require(coins==700 && inventory.Capture().SpentCoins.SequenceEqual(new[]{0,1300}),"Purchase touched another wallet or lost spend ledger.");
            InventoryStateCodec.Decode(InventoryStateCodec.Encode(inventory.Capture()));
        });
        yield return ("food_full_bag_fills_existing_stack_then_rejects_without_charge",()=>{
            var inventory=new InventorySystem(1);int coins=5000;var food=(ItemKind)8;
            Require(inventory.Purchase(0,food,0,false,false,Ground,ref coins)==InventoryResult.Success,"Food purchase rejected.");
            inventory.CreditMaterial(0,ItemKind.Wood,(StarterCells-1)*InventoryCatalog.MaterialStackLimit,Ground);
            for(int n=0;n<4;n++)Require(inventory.Purchase(0,food,0,false,false,Ground,ref coins)==InventoryResult.Success,"Existing food stack not filled in full bag.");
            var before=InventoryStateCodec.Encode(inventory.Capture());
            Require(inventory.Purchase(0,food,0,false,false,Ground,ref coins)==InventoryResult.NoSpace && coins==4500,"Overflow charged or was accepted.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())),"Failed purchase changed inventory.");
        });
    }
}
