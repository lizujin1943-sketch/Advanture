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
        private readonly ComboHud combo;
        private readonly ItemIconPainter itemIcons=new ItemIconPainter();
        private readonly ObjectivePanelMotion objectives = new ObjectivePanelMotion();
        private readonly HealthBarFeedback squadHealth = new HealthBarFeedback();
        public float ObjectiveReveal => objectives.Reveal(Time.unscaledTime);
        public void ToggleObjectives() => objectives.Toggle(Time.unscaledTime);
        public void Hide() => squadHealth.Reset();
        public GameplayHud(IslandFishingController room,FishingOverlay overlay,HudPainter painter)
        {this.room=room;this.overlay=overlay;ui=painter;commissions=new CommissionHud(room,painter);finale=new FinaleHud(room,painter);combo=new ComboHud();}

        public void Draw(HudLayout layout,float scale)
        {
            if(room.State==null || !room.TryGetLocalPlayer(out var local))return;
            if(room.State.Won){Hide();finale.DrawVictory(layout);return;}
            DrawSquad(layout);DrawWallet(layout,local);
            DrawWeapons(layout,local);
            DrawObjectives(layout);
            combo.DrawFrame(layout.Combo,room.Feedback.Combo.Sample(Time.unscaledTime));
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

        private void DrawWeapons(HudLayout layout,Fisher local)
        {
            if(room.State.Tackle?.Config.Melee.Enabled!=true || room.Construction?.Open==true)return;
            var inventory=room.State.Tackle.Inventory;var equipped=inventory.Equipped(local.Slot,EquipmentSlot.RightHand);
            for(int slot=0;slot<2;slot++)
            {
                ItemKind kind=slot==0?ItemKind.Rod:ItemKind.Weapon;InventoryItem owned=default;
                foreach(var item in inventory.Items)if(item.Owner==local.Slot && item.Kind==kind && (owned.Id==0 || item.Type>owned.Type))owned=item;
                var area=layout.Weapon(slot);bool active=equipped?.Kind==kind;
                if(active)owned=equipped.Value;
                ui.Surface(area,new Color(.025f,.095f,.12f,active?.78f:.4f));
                if(active)ui.Surface(new Rect(area.x,area.yMax-3,area.width,3),new Color(1,.78f,.3f));
                if(owned.Id!=0)itemIcons.Draw(new Rect(area.x+9,area.y+4,36,36),owned.Kind,owned.Type,tint:active?Color.white:new Color(.65f,.72f,.74f));
                ui.Text(new Rect(area.x+3,area.y+30,16,18),(slot+1).ToString(),11,HudPainter.Muted,TextAnchor.MiddleCenter);
                if(active && kind==ItemKind.Weapon && local.Melee.CooldownTicks>0)
                {float fraction=local.Melee.CooldownTicks/(float)WeaponCatalog.Get(local.Melee.WeaponType).CooldownTicks;ui.Surface(new Rect(area.x+2,area.y+2,area.width-4,(area.height-4)*fraction),new Color(.02f,.06f,.08f,.55f));}
                ui.Hint(area,owned.Id!=0?owned.Definition.Name+" · "+(slot+1):"未携带");
                bool previous=GUI.enabled;GUI.enabled=previous && owned.Id!=0 && !room.Incapacitated && room.Equipment?.Pending!=true;
                if(GUI.Button(area,GUIContent.none,GUIStyle.none))room.Equipment?.SelectTool(local,kind);
                GUI.enabled=previous;
            }
        }

        private void DrawObjectives(HudLayout layout)
        {
            bool active=room.State.Finale.Phase!=FinalePhase.Dormant;
            if(!active && room.State.Tackle?.Config.Commissions.Enabled!=true)return;
            float reveal=ObjectiveReveal;
            if(reveal>0)
            {
                if(active)finale.DrawObjective(layout.CommissionPanel(true,reveal));
                else commissions.Draw(layout.CommissionPanel(room.State.Tackle.Config.Ocean.Enabled,reveal));
            }
            var tab=layout.ObjectiveTab(reveal);ui.Surface(tab);
            ui.Text(tab,objectives.Collapsed?"›":"‹",24,HudPainter.Muted,TextAnchor.MiddleCenter,true);
            ui.Hint(tab,objectives.Collapsed?"展开任务 · J":"收起任务 · J");
            if(GUI.Button(tab,GUIContent.none,GUIStyle.none))ToggleObjectives();
        }

        private void DrawSquad(HudLayout layout)
        {
            squadHealth.BeginFrame(room.Client,room.Client?.RoundEpoch??0,room.State.Steps,Time.unscaledTime,true);
            int count=0;foreach(var player in room.State.Players)if(!player.WaitingToJoin)count++;
            var panel=layout.Squad(count);ui.Surface(panel);
            int index=0;
            foreach(var player in room.State.Players)
            {
                if(player.WaitingToJoin){squadHealth.Observe(player.Slot,0,0,true);continue;}
                float x=panel.x+8+index++*52;var tint=FishingPresentation.PlayerColors[player.Slot];
                ui.Surface(new Rect(x+5,panel.y+7,34,25),new Color(tint.r,tint.g,tint.b,.18f));
                ui.Text(new Rect(x+5,panel.y+7,34,25),(player.Slot+1).ToString(),14,tint,TextAnchor.MiddleCenter,true);
                if(player.Slot==room.LocalSlot)ui.Surface(new Rect(x+37,panel.y+7,4,4),HudPainter.White);
                int maximum=room.State.Tackle==null?100:FishingRules.Attributes(room.State,player).HealthMax;
                var health=squadHealth.Observe(player.Slot,room.State.Tackle==null?100:player.Vitals.Health,maximum);
                ui.HealthBar(new Rect(x+6,panel.y+39,32,3),health);
                if(player.Revival.Dead || player.Vitals.Downed || player.Swimming.InWater)
                    ui.Icon(new Rect(x+30,panel.y+22,16,16),player.Revival.Dead?HudIcon.Skull:player.Vitals.Downed?HudIcon.Rescue:HudIcon.SwimRing,player.Vitals.Downed?HudPainter.Health:HudPainter.White);
                if(player.Unavailable)ui.Text(new Rect(x,panel.y+44,46,15),"断线",10,HudPainter.Gold,TextAnchor.MiddleCenter);
                ui.Hint(new Rect(x,panel.y,52,52),$"P{player.Slot+1}"+(player.Slot==room.LocalSlot?" · 你":"")+(player.Unavailable?" · 断线":"")+(player.Revival.Dead?" · 死亡":player.Vitals.Downed?" · 等待救援":player.Swimming.InWater?" · 落水":""));
            }
        }

        private void DrawWallet(HudLayout layout,Fisher player)
        {
            var rect=layout.Wallet;ui.Surface(rect);
            Resource(rect.x+12,rect.y,HudIcon.Coin,room.State.Tackle==null?room.State.Coins:player.Coins,HudPainter.Gold,"金币");
        }
        private void Resource(float x,float y,HudIcon icon,int value,Color color,string label)
        {
            ui.Icon(new Rect(x,y+13,22,22),icon,color);
            ui.Text(new Rect(x+28,y+6,80,36),Compact(value),16,HudPainter.White,TextAnchor.MiddleLeft,true);
            ui.Hint(new Rect(x,y,104,48),$"{label} · {value:N0}");
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
        }

        public void DrawHelp(HudLayout layout)
        {
            var rect=layout.Help;ui.Surface(rect);ui.Text(new Rect(rect.x+26,rect.y+18,300,32),"操作指南",20,HudPainter.White,TextAnchor.MiddleLeft,true);
            if(ui.IconButton(new Rect(rect.xMax-48,rect.y+13,32,32),HudIcon.Close,"关闭 · Esc"))overlay.MenuOpen=false;
            string[] keys={"WASD","鼠标","左键","右键","V / J","R","E","Tab / I","B","滚轮"};
            string[] hints={"移动 · 第三人称按视角方向","俯视角瞄准 / 第三人称转向","出钩 · 第三人称沿视线方向","收钩 / 取消","V 切换视角 · J 收起 / 展开任务","按住救起队友","开箱 / 商店","角色与背包","扩建平台 · 1 / 2 切换材料","俯视角缩放 / 第三人称拉近拉远"};
            for(int i=0;i<keys.Length;i++)
            {float y=rect.y+65+i*32;ui.Surface(new Rect(rect.x+26,y+3,70,24),HudPainter.Track);ui.Text(new Rect(rect.x+26,y+3,70,24),keys[i],12,HudPainter.White,TextAnchor.MiddleCenter);ui.Text(new Rect(rect.x+116,y,280,30),hints[i],14,HudPainter.Muted);}
        }
    }
}
