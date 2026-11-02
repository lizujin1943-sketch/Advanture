using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    internal sealed partial class BackpackOverlay
    {
        private void DrawFoodDetails(HookState state,Fisher player,InventoryItem item,int type,bool buying,
            float x,float y,bool pending,Func<InventoryCommand,bool> request)
        {
            var food=FoodCatalog.Get(type);bool enabled=GUI.enabled;
            GUI.Label(new Rect(x,y+88,410,48),$"恢复 {food.Health} 生命 · 共享冷却 {state.Tackle.Config.FoodCooldownTicks/(float)HookGame.Rate:0.#} 秒\n每格 {food.StackLimit} 个 · 收钩后在平台上食用",text);
            if(buying)
            {
                int stock=shelf.FoodStock[type];GUI.enabled=!pending && stock>0;
                if(GUI.Button(new Rect(x,y+158,408,40),stock==0?"售罄":$"购买 1 份 · {food.Price} G",button))request(selection.Command(false));
            }
            else
            {
                bool full=player.Vitals.Health>=FishingRules.Attributes(state,player).HealthMax;
                GUI.enabled=!pending && !full && player.FoodCooldownTicks==0;
                string label=full?"生命已满":player.FoodCooldownTicks>0?$"冷却 {player.FoodCooldownTicks/(float)HookGame.Rate:0.0} 秒":"食用 1 份";
                if(GUI.Button(new Rect(x,y+145,408,36),label,button))request(new InventoryCommand(InventoryAction.Use,item.Id));
                GUI.enabled=!pending;amount=Mathf.Clamp(amount,1,item.Count);
                GUI.Label(new Rect(x,y+193,400,24),$"数量 {amount} / {item.Count}",small);
                amount=Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x,y+224,398,20),amount,1,item.Count));
                if(GUI.Button(new Rect(x,y+254,128,33),"移动",button)){placing=1;rotated=false;}
                GUI.enabled=!pending && amount<item.Count;
                if(GUI.Button(new Rect(x+139,y+254,128,33),"拆分",button))placing=2;
                GUI.enabled=!pending && !player.Swimming.InWater;
                if(GUI.Button(new Rect(x+278,y+254,130,33),"放到平台",button))request(new InventoryCommand(InventoryAction.Drop,item.Id,amount:amount));
                GUI.enabled=enabled;
                DrawQuickBindings(state.Tackle.Inventory,player.Slot,item,x,y+362,pending,request);
            }
            GUI.enabled=enabled;
        }
        private void DrawQuickBindings(InventoryState inventory,int owner,InventoryItem item,float x,float y,bool pending,Func<InventoryCommand,bool> request)
        {
            if(!QuickbarRules.CanBind(item))return;
            GUI.Label(new Rect(x,y,280,19),"放入快捷栏 · 1–9 选择，左键使用",small);
            bool enabled=GUI.enabled;int bound=-1;
            for(int n=0;n<QuickbarRules.SlotCount;n++)
            {
                var binding=inventory.Quickbar[owner*QuickbarRules.SlotCount+n];
                bool selected=item.Kind==ItemKind.Food?binding.Kind==ItemKind.Food && binding.Type==item.Type:binding.ItemId==item.Id;
                if(selected)bound=n;
                var rect=new Rect(x+n*46,y+22,40,25);if(selected)Fill(rect,accent);
                GUI.enabled=!pending;
                if(GUI.Button(rect,(n+1).ToString(),button))request(new InventoryCommand(InventoryAction.BindQuickSlot,item.Id,amount:n+1));
            }
            GUI.enabled=!pending && bound>=0;
            if(GUI.Button(new Rect(x+324,y-1,84,21),"取消绑定",button))request(new InventoryCommand(InventoryAction.ClearQuickSlot,bound+1));
            GUI.enabled=enabled;
        }
    }
}
