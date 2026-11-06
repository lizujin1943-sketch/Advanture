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
            // 手雷沿用同一处纯文字读数，只有已确认装备时显示；不新增常驻快捷栏或按键教学。
            if(room.HoldingGrenade)
                ui.FloatingText(new Rect(rect.x-40,rect.y+26,rect.width+40,54),"手榴弹 ×1",26,Color.white,TextAnchor.MiddleRight);
            if(room.Grenades?.Message!=null)
                ui.FloatingText(new Rect(rect.x-160,rect.y-4,rect.width+160,28),room.Grenades.Message,16,HudPainter.Gold,TextAnchor.MiddleRight);
            var firearm=ActiveToolView.Firearm(room.State,player);
            if(firearm==null)return;
            var loaded=room.State.Tackle.Inventory.ActiveItem(player.Slot);
            if(!loaded.HasValue)return;
            int ammo=loaded.Value.LoadedAmmo;
            // 把常驻读数贴近底部，临时换弹文字放在上方，避免为了预留提示而把整组数字抬高。
            float y=rect.y+26;
            int? reserve=FirearmRules.ReserveRounds(room.State,player,loaded.Value);
            string supply=reserve?.ToString()??"∞";
            // Anchor from the right so a three-digit boss reserve fits alongside the magazine.
            float width=Mathf.Max(44,supply.Length*23);
            float end=rect.xMax;
            ui.FloatingText(new Rect(end-width-94,y,68,54),ammo.ToString(),40,ammo>0?Color.white:HudPainter.Gold,TextAnchor.MiddleRight);
            ui.FloatingText(new Rect(end-width-23,y,22,54),"/",36,HudPainter.DisplayColor(new Color(.13f,.68f,.94f)));
            ui.FloatingText(new Rect(end-width,y,width,54),supply,reserve.HasValue?36:44,reserve==0?HudPainter.Gold:Color.white,TextAnchor.MiddleRight);
            // 常驻 HUD 不显示 R 按键教学；真正换弹期间仍提供确认态的进度反馈。
            if(player.Firearm.Reloading)
                ui.FloatingText(new Rect(rect.x,rect.y,rect.width,26),
                    $"换弹中 · {player.Firearm.ReloadTicks/(float)HookGame.Rate:0.0}s",16,HudPainter.Gold,TextAnchor.MiddleRight);
            else if(ammo==0 && reserve==0)
                ui.FloatingText(new Rect(rect.x-90,rect.y,rect.width+90,26),"备弹耗尽 · 到军火铺补给",16,HudPainter.Gold,TextAnchor.MiddleRight);
        }
    }
}
