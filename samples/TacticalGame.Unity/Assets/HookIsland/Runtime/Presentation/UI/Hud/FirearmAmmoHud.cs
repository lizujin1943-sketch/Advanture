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
            var loaded=room.State.Tackle.Inventory.ActiveItem(player.Slot);
            int ammo=loaded?.LoadedAmmo??0;
            // 把常驻读数贴近底部，临时换弹文字放在上方，避免为了预留提示而把整组数字抬高。
            float y=rect.y+26;
            ui.FloatingText(new Rect(rect.x,y,72,54),ammo.ToString(),40,ammo>0?Color.white:HudPainter.Gold,TextAnchor.MiddleRight);
            ui.FloatingText(new Rect(rect.x+82,y,22,54),"/",36,HudPainter.DisplayColor(new Color(.13f,.68f,.94f)));
            // 当前玩法仍为无限后备弹药，只展示既有规则；HUD 不扣弹、不开始换弹，也不推测完成时间。
            // 无限符号的字形高度天然低于数字，放大字号并加粗；与弹数共用同一行中心，不再单独下沉。
            ui.FloatingText(new Rect(rect.x+110,y,40,54),"∞",44,Color.white,TextAnchor.MiddleRight);
            // 常驻 HUD 不显示 R 按键教学；真正换弹期间仍提供确认态的进度反馈。
            if(player.Firearm.Reloading)
                ui.FloatingText(new Rect(rect.x,rect.y,rect.width,26),
                    $"换弹中 · {player.Firearm.ReloadTicks/(float)HookGame.Rate:0.0}s",16,HudPainter.Gold,TextAnchor.MiddleRight);
        }
    }
}
