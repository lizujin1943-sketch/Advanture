using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>确认态的悬浮 HUD。任务显示由视图配置控制，不影响模拟中的任务推进或结算。</summary>
    public sealed class GameplayHud
    {
        private readonly IslandFishingController room;
        private readonly FishingOverlay overlay;
        private readonly HudPainter ui;
        private readonly CommissionHud commissions;
        private readonly FinaleHud finale;
        private readonly ComboHud combo;
        private readonly FirearmAmmoHud ammo;
        private readonly FirearmReticleHud reticle;
        private readonly LocalHealthHud localHealth;
        private readonly HealingChargesHud healing;
        private readonly ObjectivePanelMotion objectives = new ObjectivePanelMotion(true);
        private readonly HealthBarFeedback squadHealth = new HealthBarFeedback();
        public float ObjectiveReveal => overlay.ShowObjectives?objectives.Reveal(Time.unscaledTime):0;
        public void ToggleObjectives(){if(overlay.ShowObjectives)objectives.Toggle(Time.unscaledTime);}
        public void Hide() => squadHealth.Reset();
        public GameplayHud(IslandFishingController room,FishingOverlay overlay,HudPainter painter)
        {this.room=room;this.overlay=overlay;ui=painter;commissions=new CommissionHud(room,painter);finale=new FinaleHud(room,painter);combo=new ComboHud();ammo=new FirearmAmmoHud(room,painter);reticle=new FirearmReticleHud(room,painter);localHealth=new LocalHealthHud(painter);healing=new HealingChargesHud(painter);}

        public void Draw(HudLayout layout,float scale)
        {
            if(room.State==null || !room.TryGetLocalPlayer(out var local))return;
            if(room.State.Won){Hide();finale.DrawVictory(layout);return;}
            reticle.Draw(local,scale);
            DrawSquad(layout);DrawWallet(layout,local);
            // HP 与装备提供的上限必须读取同一份确认态；预测回滚不能让固定血条反复掉血。
            // 此处不依赖人物投影或镜头，因此第一人称、人物出画面和倒地时仍能显示。
            if(room.State.Tackle!=null && !local.WaitingToJoin)
            {
                int maximum=FishingRules.Attributes(room.State,local).HealthMax;
                localHealth.Draw(layout.LocalHealth,local,maximum,
                    squadHealth.Observe(local.Slot,local.Vitals.Health,maximum),Time.unscaledTime);
                // 医疗罐和生命读取同一份确认态；建造菜单在两者上方，不遮挡个人次数。
                healing.Draw(layout.LocalHealing,local,room.State.Tackle.Config.Healing);
            }
            ammo.Draw(layout.FirearmAmmo,local);
            DrawObjectives(layout);
            combo.DrawFrame(layout.Combo,room.Feedback.Combo.Sample(Time.unscaledTime));
            // Esc / Tab / B / F1 仍由各自输入模块处理；不再为这些功能常驻绘制大块按钮。
            DrawWorld(scale,layout.Height);
            if(room.Construction?.Open==true)DrawConstruction(layout.Construction);
            DrawFeedback(layout);
        }

        private void DrawObjectives(HudLayout layout)
        {
            if(!overlay.ShowObjectives)return;
            bool active=room.State.Finale.Phase!=FinalePhase.Dormant;
            if(!active && room.State.Tackle?.Config.Commissions.Enabled!=true)return;
            if(active)finale.DrawSummary(layout.ObjectiveSummary);
            else commissions.DrawSummary(layout.ObjectiveSummary);
            float reveal=ObjectiveReveal;
            if(reveal>0)
            {
                if(active)finale.DrawObjective(layout.CommissionPanel(true,reveal));
                else commissions.Draw(layout.CommissionPanel(room.State.Tackle.Config.Ocean.Enabled,reveal));
            }
            var tab=layout.ObjectiveTab();
            ui.FloatingText(tab,objectives.Collapsed?"J 详情":"J 收起",15,Color.white,TextAnchor.MiddleRight);
            ui.Hint(tab,objectives.Collapsed?"展开任务 · J":"收起任务 · J");
            if(GUI.Button(tab,GUIContent.none,GUIStyle.none))ToggleObjectives();
        }

        private void DrawSquad(HudLayout layout)
        {
            squadHealth.BeginFrame(room.Client,room.Client?.RoundEpoch??0,room.State.Steps,Time.unscaledTime,true);
            int count=TeammateCount(room.State,room.LocalSlot);
            bool objectivesVisible=room.State.Finale.Phase!=FinalePhase.Dormant || room.State.Tackle?.Config.Commissions.Enabled==true;
            var panel=layout.Squad(count,objectivesVisible?ObjectiveReveal:0,room.State.Tackle?.Config.Ocean.Enabled==true || room.State.Finale.Phase!=FinalePhase.Dormant);
            int index=0;
            foreach(var player in room.State.Players)
            {
                if(player.WaitingToJoin){squadHealth.Observe(player.Slot,0,0,true);continue;}
                if(player.Slot==room.LocalSlot)continue;
                float x=panel.x+index++*80;var tint=FishingPresentation.PlayerColors[player.Slot];
                ui.FloatingText(new Rect(x,panel.y,68,25),"P"+(player.Slot+1),16,Color.white);
                HudPainter.Fill(new Rect(x,panel.y+29,64,2),tint);
                int maximum=room.State.Tackle==null?100:FishingRules.Attributes(room.State,player).HealthMax;
                var health=squadHealth.Observe(player.Slot,room.State.Tackle==null?100:player.Vitals.Health,maximum);
                ui.HealthBar(new Rect(x,panel.y+34,64,4),health);
                if(player.Revival.Dead || player.Vitals.Downed || player.Swimming.InWater)
                    ui.Icon(new Rect(x+48,panel.y+3,16,16),player.Revival.Dead?HudIcon.Skull:player.Vitals.Downed?HudIcon.Rescue:HudIcon.SwimRing,player.Vitals.Downed?HudPainter.Health:HudPainter.White);
                if(player.Unavailable)ui.FloatingText(new Rect(x,panel.y+38,68,14),"断线",10,HudPainter.Gold,italic:false);
                ui.Hint(new Rect(x,panel.y,72,52),$"P{player.Slot+1}"+(player.Unavailable?" · 断线":"")+(player.Revival.Dead?" · 死亡":player.Vitals.Downed?" · 等待救援":player.Swimming.InWater?" · 落水":""));
            }
        }

        /// <summary>渲染和鼠标遮挡共用实际队友数，避免单人或等待加入的席位留下空面板。</summary>
        public static int TeammateCount(HookState state,int localSlot)
        {int count=0;if(state!=null)foreach(var player in state.Players)if(!player.WaitingToJoin && player.Slot!=localSlot)count++;return count;}

        private void DrawWallet(HudLayout layout,Fisher player)
        {
            var rect=layout.Wallet;
            int value=room.State.Tackle==null?room.State.Coins:player.Coins;
            string text=Compact(value);
            // 整组右对齐；图标随实际字宽移动，不能把数字塞进一个过宽的固定右对齐区域。
            float numberWidth=ui.TextWidth(text,28)+4,numberX=rect.xMax-numberWidth;
            ui.Icon(new Rect(numberX-34,rect.y+9,30,30),HudIcon.Coin,HudPainter.Gold);
            ui.FloatingText(new Rect(numberX,rect.y,numberWidth,48),text,28,Color.white);
            ui.Hint(new Rect(numberX-34,rect.y,numberWidth+34,48),$"金币 · {value:N0}");
        }
        private static string Compact(int value)=>value<10000?value.ToString("N0"):value<1000000?(value/1000f).ToString("0.#")+"k":(value/1000000f).ToString("0.#")+"m";

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
            if(message==null)message=build.PreviewResult==BuildResult.Success?"按住左键拖动 · 松开铺设":PlatformConstruction.Explain(build.PreviewResult);
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
        }

        public void DrawHelp(HudLayout layout)
        {
            var rect=layout.Help;ui.Surface(rect);ui.Text(new Rect(rect.x+26,rect.y+18,300,32),"操作指南",20,HudPainter.White,TextAnchor.MiddleLeft,true);
            if(ui.IconButton(new Rect(rect.xMax-48,rect.y+13,32,32),HudIcon.Close,"关闭 · Esc"))overlay.MenuOpen=false;
            string[] keys={"WASD","鼠标","左键","右键","C / V","R","E","Tab / I","B / 1–9","滚轮"};
            string[] hints={"移动 · 持枪时按视角方向","鼠标瞄准 / 转向","开火 / 出钩 · AK47 按住连射","狙击枪按住开镜 / 收钩 / 取消","C 切换视角 · V 回血","R 换弹 · 按住优先救起队友","开箱 / 商店",overlay.ShowObjectives?"角色与背包 · J 任务":"角色与背包","B 扩建 · 1–9 选择快捷物品","俯视角缩放 / 第三人称拉近拉远"};
            for(int i=0;i<keys.Length;i++)
            {float y=rect.y+65+i*32;ui.Surface(new Rect(rect.x+26,y+3,70,24),HudPainter.Track);ui.Text(new Rect(rect.x+26,y+3,70,24),keys[i],12,HudPainter.White,TextAnchor.MiddleCenter);ui.Text(new Rect(rect.x+116,y,280,30),hints[i],14,HudPainter.Muted);}
        }
    }
}
