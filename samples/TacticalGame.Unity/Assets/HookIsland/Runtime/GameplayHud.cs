using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Compact presentation of confirmed state. Buttons use existing local intent adapters.</summary>
    public sealed class GameplayHud
    {
        private readonly IslandFishingController room;
        private readonly FishingOverlay overlay;
        private readonly HudPainter ui;
        private readonly CommissionHud commissions;
        private readonly FinaleHud finale;
        private readonly MerchantHud merchants;
        public GameplayHud(IslandFishingController room,FishingOverlay overlay,HudPainter painter)
        {this.room=room;this.overlay=overlay;ui=painter;commissions=new CommissionHud(room,painter);finale=new FinaleHud(room,painter);merchants=new MerchantHud(room,painter);}

        public void Draw(HudLayout layout,float scale)
        {
            if(room.State==null || !room.TryGetLocalPlayer(out var local))return;
            if(room.State.Won){finale.DrawVictory(layout);return;}
            DrawSquad(layout);DrawWallet(layout,local);
            if(room.State.Finale.Phase!=FinalePhase.Dormant)finale.DrawObjective(layout);else commissions.Draw(layout);
            merchants.Draw(layout,scale,local);
            if(room.State.Tackle!=null)DrawVitals(layout.Vitals,local);
            if(ui.IconButton(layout.Menu,HudIcon.Menu,"菜单 · Esc"))overlay.OpenMenu();
            bool enabled=GUI.enabled;GUI.enabled=!room.Incapacitated;
            if(ui.IconButton(layout.Tool(0),HudIcon.Backpack,"角色与背包 · Tab","TAB"))room.Equipment?.ToggleInventory();
            GUI.enabled=!room.Incapacitated && room.Construction!=null && room.Map.Platform!=null;
            if(ui.IconButton(layout.Tool(1),HudIcon.Build,"扩建平台 · B","B",room.Construction?.Open==true))room.Construction.Toggle();
            GUI.enabled=enabled;
            if(ui.IconButton(layout.Tool(2),HudIcon.Help,"操作指南 · F1","F1"))overlay.OpenHelp();
            DrawWorld(scale,layout.Height);
            if(room.Construction?.Open==true)DrawConstruction(layout.Construction);
            DrawFeedback(layout);
        }

        private void DrawSquad(HudLayout layout)
        {
            int count=0;foreach(var player in room.State.Players)if(!player.WaitingToJoin)count++;
            var panel=layout.Squad(count);ui.Surface(panel);
            int index=0;
            foreach(var player in room.State.Players)
            {
                if(player.WaitingToJoin)continue;
                float x=panel.x+8+index++*52;var tint=FishingPresentation.PlayerColors[player.Slot];
                ui.Surface(new Rect(x+5,panel.y+7,34,25),new Color(tint.r,tint.g,tint.b,.18f));
                ui.Text(new Rect(x+5,panel.y+7,34,25),(player.Slot+1).ToString(),14,tint,TextAnchor.MiddleCenter,true);
                if(player.Slot==room.LocalSlot)ui.Surface(new Rect(x+37,panel.y+7,4,4),HudPainter.White);
                float fraction=room.State.Tackle==null?1:HudPainter.Fraction(player.Vitals.Health,FishingRules.Attributes(room.State,player).HealthMax);
                ui.Bar(new Rect(x+6,panel.y+39,32,3),fraction,tint);
                if(player.Revival.Dead || player.Vitals.Downed || player.Swimming.InWater)
                    ui.Icon(new Rect(x+30,panel.y+22,16,16),player.Revival.Dead?HudIcon.Skull:player.Vitals.Downed?HudIcon.Rescue:HudIcon.SwimRing,player.Vitals.Downed?HudPainter.Coral:HudPainter.White);
                if(player.Unavailable)ui.Text(new Rect(x,panel.y+44,46,15),"断线",10,HudPainter.Gold,TextAnchor.MiddleCenter);
                ui.Hint(new Rect(x,panel.y,52,52),$"P{player.Slot+1}"+(player.Slot==room.LocalSlot?" · 你":"")+(player.Unavailable?" · 断线":"")+(player.Revival.Dead?" · 死亡":player.Vitals.Downed?" · 等待救援":player.Swimming.InWater?" · 落水":""));
            }
        }

        private void DrawWallet(HudLayout layout,Fisher player)
        {
            bool materials=room.Map.Platform!=null;var rect=layout.Resources(materials);ui.Surface(rect);
            Resource(rect.x+12,rect.y,HudIcon.Coin,room.State.Tackle==null?room.State.Coins:player.Coins,HudPainter.Gold,"金币");
            if(!materials)return;
            HudPainter.Fill(new Rect(rect.x+94,rect.y+14,1,20),HudPainter.Track);
            Resource(rect.x+105,rect.y,HudIcon.Wood,player.Wood,new Color(.84f,.70f,.49f),"木材");
            HudPainter.Fill(new Rect(rect.x+186,rect.y+14,1,20),HudPainter.Track);
            Resource(rect.x+197,rect.y,HudIcon.Bone,player.Fishbones,HudPainter.White,"鱼骨");
        }
        private void Resource(float x,float y,HudIcon icon,int value,Color color,string label)
        {
            ui.Icon(new Rect(x,y+13,22,22),icon,color);
            ui.Text(new Rect(x+28,y+6,52,36),Compact(value),16,HudPainter.White,TextAnchor.MiddleLeft,true);
            ui.Hint(new Rect(x,y,80,48),$"{label} · {value:N0}");
        }
        private static string Compact(int value)=>value<10000?value.ToString("N0"):value<1000000?(value/1000f).ToString("0.#")+"k":(value/1000000f).ToString("0.#")+"m";

        private void DrawVitals(Rect rect,Fisher player)
        {
            ui.Surface(rect);var stats=FishingRules.Attributes(room.State,player);
            float stamina=HudPainter.Fraction(player.Stamina,stats.StaminaMax);var tint=HudPainter.StaminaColor(stamina);
            ui.Icon(new Rect(rect.x+14,rect.y+12,18,18),HudIcon.Heart,HudPainter.Coral);
            ui.Bar(new Rect(rect.x+42,rect.y+19,180,4),HudPainter.Fraction(player.Vitals.Health,stats.HealthMax),HudPainter.Coral);
            ui.Text(new Rect(rect.x+234,rect.y+10,110,22),$"{player.Vitals.Health} / {stats.HealthMax}",12,HudPainter.Muted,TextAnchor.MiddleRight);
            ui.Hint(new Rect(rect.x+10,rect.y+7,338,27),"生命");
            ui.Icon(new Rect(rect.x+12,rect.y+41,22,22),HudIcon.Stamina,tint);
            ui.Bar(new Rect(rect.x+42,rect.y+46,236,14),stamina,tint,true);
            ui.Text(new Rect(rect.x+288,rect.y+39,56,26),Mathf.CeilToInt(player.Stamina/(float)StaminaSystem.UnitsPerPoint).ToString(),17,tint,TextAnchor.MiddleRight,true);
            ui.Hint(new Rect(rect.x+10,rect.y+37,338,29),$"耐力 · {player.Stamina/(float)StaminaSystem.UnitsPerPoint:0.#} / {stats.StaminaMax/(float)StaminaSystem.UnitsPerPoint:0.#}");
            bool resting=!player.Vitals.Downed && player.Phase==HookPhase.Ready && !player.Swimming.Active && player.Stamina<stats.StaminaMax;
            if(resting)
            {
                var status=new Rect(rect.xMax-32,rect.y-28,24,24);ui.Surface(status);
                ui.Icon(new Rect(status.x+3,status.y+3,18,18),HudIcon.Recover,player.RegenDelay>0?HudPainter.Muted:HudPainter.Mint);
                ui.Hint(status,player.RegenDelay>0?$"{player.RegenDelay/30f:0.0} 秒后恢复":"耐力恢复中");
            }
            if(player.Swimming.InWater)
            {
                bool ring=SwimRingCatalog.Equipped(room.State.Tackle.Inventory,player.Slot)!=null;
                var status=new Rect(rect.x,rect.y-32,32,26);ui.Surface(status);
                ui.Icon(new Rect(status.x+5,status.y+2,22,22),ring?HudIcon.SwimRing:HudIcon.Alert,ring?HudPainter.Mint:HudPainter.Coral);
                ui.Hint(status,ring?"游泳圈保护中":"落水 · 持续失血，无法出竿");
            }
            if(player.Vitals.Hunger<stats.HungerMax*.3f)
            {
                var status=new Rect(rect.x+40,rect.y-32,32,26);ui.Surface(status);ui.Icon(new Rect(status.x+5,status.y+2,22,22),HudIcon.Hunger,HudPainter.Gold);ui.Hint(status,"饥饿值偏低");
            }
        }

        private void DrawConstruction(Rect rect)
        {
            var build=room.Construction;ui.Surface(rect);
            ui.Icon(new Rect(rect.x+14,rect.y+13,20,20),HudIcon.Build,HudPainter.Mint);
            ui.Text(new Rect(rect.x+42,rect.y+10,150,26),"扩建",15);
            if(ui.IconButton(new Rect(rect.xMax-40,rect.y+5,32,32),HudIcon.Close,"关闭 · B"))build.Close();
            for(int i=0;i<2;i++)
            {
                var kind=i==0?TileKind.Wood:TileKind.Fishbone;var card=new Rect(rect.x+14+i*120,rect.y+49,112,64);
                ui.Surface(card,build.Selected==kind?new Color(.14f,.37f,.33f):new Color(.10f,.19f,.22f));
                ui.Icon(new Rect(card.x+10,card.y+8,26,26),i==0?HudIcon.Wood:HudIcon.Bone,build.Selected==kind?HudPainter.Mint:HudPainter.White);
                ui.Text(new Rect(card.x+42,card.y+8,44,26),"× "+room.Map.Platform.Cost(kind),15);
                ui.Text(new Rect(card.x+10,card.y+37,86,18),i==0?"木板":"鱼骨架",12,HudPainter.Muted);
                ui.Key(new Rect(card.xMax-23,card.y+41,16,15),(i+1).ToString());
                if(GUI.Button(card,GUIContent.none,GUIStyle.none))build.Select(kind);
            }
            string message=build.Message;
            if(message==null)message=build.PreviewResult==BuildResult.Success?"左键铺设 · 右键取消":PlatformConstruction.Explain(build.PreviewResult);
            if(message=="铺设完成 · 全员可通行")message="铺设完成";
            ui.Icon(new Rect(rect.x+14,rect.y+129,16,16),build.PreviewResult==BuildResult.Success?HudIcon.Check:HudIcon.Alert,build.PreviewResult==BuildResult.Success?HudPainter.Mint:HudPainter.Gold);
            ui.Text(new Rect(rect.x+38,rect.y+121,208,32),message,12,HudPainter.Muted);
        }

        private void DrawFeedback(HudLayout layout)
        {
            var message=room.Treasures?.Feedback??room.Equipment?.Message;
            // Escapes already have their brief world-anchored cue. Do not repeat the instructional toast.
            if(string.IsNullOrEmpty(message) || message.StartsWith("耐力耗尽，鱼挣脱了"))return;
            if(message=="操作已确认 · 背包已同步" || message=="已完成 · 装备与金币已同步")return;
            var rect=new Rect(layout.Width/2-260,layout.Height-153,520,32);ui.Surface(rect);
            ui.Icon(new Rect(rect.x+9,rect.y+7,18,18),HudIcon.Alert,HudPainter.Gold);
            ui.Text(new Rect(rect.x+36,rect.y,rect.width-48,32),message,13);
        }

        private void DrawWorld(float scale,float height)
        {
            if(room.ViewCamera==null || room.Presentation==null)return;
            if(room.NearShop && !room.Incapacitated && room.Construction?.Open!=true && (room.Treasures?.Nearest()??-1)<0)
            {
                var point=room.Map.Platform==null?new Point(HookUpgrades.ShopX,HookUpgrades.ShopZ):MerchantRules.Position(room.Map.Fishing,room.NearestMerchantId);
                var shop=room.ViewCamera.WorldToScreenPoint(new Vector3(point.X/1000f,room.Map.Platform==null?4.5f:1.8f,point.Z/1000f));
                if(shop.z>0)
                {
                    var rect=new Rect(shop.x/scale-30,height-shop.y/scale-22,60,30);ui.Surface(rect);
                    ui.Icon(new Rect(rect.x+6,rect.y+4,22,22),HudIcon.Shop,HudPainter.Gold);ui.Key(new Rect(rect.x+34,rect.y+6,18,18),"E");ui.Hint(rect,"商店");
                }
            }
            foreach(var player in room.State.Players)
            {
                if(player.WaitingToJoin)continue;
                var point=room.ViewCamera.WorldToScreenPoint(room.Presentation.PlayerPosition(player.Slot)+Vector3.up*(room.Map.Platform==null?2.2f:1.2f));
                if(point.z<=0)continue;
                var rect=new Rect(point.x/scale-12,height-point.y/scale-15,24,20);ui.Surface(rect,new Color(.03f,.10f,.13f,.7f));
                ui.Text(rect,(player.Slot+1).ToString(),11,FishingPresentation.PlayerColors[player.Slot],TextAnchor.MiddleCenter,true);
                if(player.Unavailable)
                {var status=new Rect(rect.xMax+3,rect.y,36,20);ui.Surface(status);ui.Text(status,"断线",10,HudPainter.Gold,TextAnchor.MiddleCenter);}
                if(player.PulledBy>=0 || player.Shopping || player.ProtectionTicks>0)
                    ui.Icon(new Rect(rect.xMax+2,rect.y,18,18),player.PulledBy>=0?HudIcon.Hook:HudIcon.Shield,player.PulledBy>=0?HudPainter.Gold:HudPainter.Mint);
            }
            foreach(var pop in room.Presentation.Popups)
            {
                var point=room.ViewCamera.WorldToScreenPoint(pop.Position+Vector3.up*(1.5f-(pop.Until-Time.unscaledTime)));if(point.z<=0)continue;
                float x=point.x/scale,y=height-point.y/scale-30;ui.Icon(new Rect(x-38,y+5,22,22),HudIcon.Coin,HudPainter.Gold);
                ui.Text(new Rect(x-10,y,100,32),"+"+pop.Value,21,HudPainter.Gold,TextAnchor.MiddleLeft,true);
            }
        }

        public void DrawHelp(HudLayout layout)
        {
            var rect=layout.Help;ui.Surface(rect);ui.Text(new Rect(rect.x+26,rect.y+18,300,32),"操作指南",20,HudPainter.White,TextAnchor.MiddleLeft,true);
            if(ui.IconButton(new Rect(rect.xMax-48,rect.y+13,32,32),HudIcon.Close,"关闭 · Esc"))overlay.MenuOpen=false;
            string[] keys={"WASD","左键","右键","R","E","Tab / I","B","滚轮"};
            string[] hints={"移动","瞄准出钩","收钩 / 取消","按住救起队友","开箱 / 商店","角色与背包","扩建平台 · 1 / 2 切换材料","缩放视野"};
            for(int i=0;i<keys.Length;i++)
            {float y=rect.y+65+i*32;ui.Surface(new Rect(rect.x+26,y+3,70,24),HudPainter.Track);ui.Text(new Rect(rect.x+26,y+3,70,24),keys[i],12,HudPainter.White,TextAnchor.MiddleCenter);ui.Text(new Rect(rect.x+116,y,280,30),hints[i],14,HudPainter.Muted);}
        }
    }
}
