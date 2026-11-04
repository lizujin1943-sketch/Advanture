using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    internal sealed partial class BackpackOverlay
    {
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
