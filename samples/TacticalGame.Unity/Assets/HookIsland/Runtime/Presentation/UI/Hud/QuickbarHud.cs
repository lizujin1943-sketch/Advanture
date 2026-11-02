using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Nine confirmed inventory bindings. Selection submits intent; slots never own items.</summary>
    public sealed class QuickbarHud
    {
        private readonly IslandFishingController room;
        private readonly HudPainter ui;
        private readonly ItemIconPainter icons=new ItemIconPainter();
        public QuickbarHud(IslandFishingController room,HudPainter ui){this.room=room;this.ui=ui;}
        public void Draw(HudLayout layout,Fisher player)
        {
            if(room.State.Tackle==null || room.Construction?.Open==true)return;
            var inventory=room.State.Tackle.Inventory;
            for(int n=0;n<QuickbarRules.SlotCount;n++)
            {
                var b=inventory.Quickbar[player.Slot*QuickbarRules.SlotCount+n];var rect=layout.QuickSlot(n);
                int count=QuickbarRules.Count(inventory,player.Slot,n);bool selected=player.SelectedQuickSlot==n;
                ui.Surface(rect,selected?new Color(.85f,.63f,.26f,.95f):new Color(.19f,.33f,.36f,.55f));
                var inside=new Rect(rect.x+2,rect.y+2,rect.width-4,rect.height-4);
                ui.Surface(inside,selected?new Color(.055f,.15f,.17f,.94f):new Color(.025f,.095f,.12f,.75f));
                if(!b.Empty)icons.Draw(new Rect(rect.x+8,rect.y+5,38,38),b.Kind,b.Type,tint:count>0?Color.white:new Color(.65f,.7f,.7f,.3f));
                float cooldown=0;int ticks=0;
                if(b.Kind==ItemKind.Food){ticks=player.FoodCooldownTicks;cooldown=ticks/(float)room.State.Tackle.Config.FoodCooldownTicks;}
                else if(b.Kind==ItemKind.Weapon && player.Melee.CooldownTicks>0){ticks=player.Melee.CooldownTicks;cooldown=ticks/(float)WeaponCatalog.Get(b.Type).CooldownTicks;}
                if(cooldown>0)
                {
                    ui.Surface(new Rect(inside.x,inside.yMax-inside.height*cooldown,inside.width,inside.height*cooldown),new Color(.015f,.04f,.05f,.7f));
                    ui.Text(new Rect(rect.x,rect.y+13,rect.width,26),(ticks/(float)HookGame.Rate).ToString("0.0"),16,HudPainter.White,TextAnchor.MiddleCenter,true);
                    ui.Surface(new Rect(rect.x+3,rect.yMax-3,(rect.width-6)*(1-cooldown),2),HudPainter.Gold);
                }
                ui.Text(new Rect(rect.x+5,rect.yMax-17,16,14),(n+1).ToString(),10,selected?HudPainter.Gold:HudPainter.Muted);
                if(b.Kind==ItemKind.Food)ui.Text(new Rect(rect.x+23,rect.yMax-18,25,15),count.ToString(),12,count>0?HudPainter.White:HudPainter.Muted,TextAnchor.MiddleRight,true);
                ui.Hint(rect,b.Empty?"空快捷位 · 在背包中设置":InventoryCatalog.Get(b.Kind,b.Type).Name+(b.Kind==ItemKind.Food?$" · +{FoodCatalog.Get(b.Type).Health} 生命 · 左键食用":""));
                bool enabled=GUI.enabled;GUI.enabled=enabled && !room.Incapacitated && room.Equipment?.Pending!=true;
                if(GUI.Button(rect,GUIContent.none,GUIStyle.none))room.Equipment?.SelectQuickSlot(n);
                GUI.enabled=enabled;
            }
        }
    }
}
