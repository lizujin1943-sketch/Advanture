using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>右下角纯文字弹药：读取确认态的已装备枪械，不绘制武器图标、不持有物品。</summary>
    public sealed class FirearmAmmoHud
    {
        private readonly IslandFishingController room;
        private readonly HudPainter ui;
        public FirearmAmmoHud(IslandFishingController room,HudPainter ui){this.room=room;this.ui=ui;}
        public void Draw(Rect rect,Fisher player)
        {
            if(room.State.Tackle==null || room.Construction?.Open==true)return;
            var firearm=ActiveToolView.Firearm(room.State,player);
            if(firearm==null)return;
            var loaded=room.State.Tackle.Inventory.Equipped(player.Slot,EquipmentSlot.RightHand);
            int ammo=loaded?.LoadedAmmo??0;
            ui.FloatingText(new Rect(rect.x,rect.y,72,54),ammo.ToString(),40,ammo>0?Color.white:HudPainter.Gold,TextAnchor.MiddleRight);
            ui.FloatingText(new Rect(rect.x+82,rect.y+4,22,48),"/",36,HudPainter.DisplayColor(new Color(.13f,.68f,.94f)));
            // 当前玩法仍为无限后备弹药，只展示既有规则；HUD 不扣弹、不开始换弹，也不推测完成时间。
            ui.FloatingText(new Rect(rect.x+113,rect.y+10,37,40),"∞",28,Color.white,bold:false);
            // 常驻 HUD 不显示 R 按键教学；真正换弹期间仍提供确认态的进度反馈。
            if(player.Firearm.Reloading)
                ui.FloatingText(new Rect(rect.x,rect.y+56,rect.width,26),
                    $"换弹中 · {player.Firearm.ReloadTicks/(float)HookGame.Rate:0.0}s",16,HudPainter.Gold,TextAnchor.MiddleRight);
        }
    }
}
