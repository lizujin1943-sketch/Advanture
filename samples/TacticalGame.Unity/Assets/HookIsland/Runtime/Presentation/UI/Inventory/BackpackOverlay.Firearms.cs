using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    internal sealed partial class BackpackOverlay
    {
        private void DrawAmmunitionDetails(bool finite,Fisher player,InventoryItem item,int type,float x,float y,bool pending,Func<InventoryCommand,bool> request)
        {
            var ammo=AmmunitionCatalog.Get(type);bool enabled=GUI.enabled;
            GUI.Label(new Rect(x,y+88,410,50),(finite?"换弹时消耗同口径备弹":"当前使用无限备用弹匣")+$"\n每格最多 {ammo.StackLimit} 发",text);
            amount=Mathf.Clamp(amount,1,item.Count);GUI.enabled=enabled && !pending;
            GUI.Label(new Rect(x,y+151,400,25),$"数量 {amount} / {item.Count}",small);
            amount=Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x,y+184,398,20),amount,1,item.Count));
            if(GUI.Button(new Rect(x,y+222,128,33),"移动整堆",button))placing=1;
            GUI.enabled=enabled && !pending && amount<item.Count;
            if(GUI.Button(new Rect(x+139,y+222,128,33),"拆分",button))placing=2;
            GUI.enabled=enabled && !pending && !player.Swimming.InWater;
            if(GUI.Button(new Rect(x+278,y+222,130,33),"放到平台",button))request(new InventoryCommand(InventoryAction.Drop,item.Id,amount:amount));
            GUI.enabled=enabled;
        }
    }
}
