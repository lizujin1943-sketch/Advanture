using UnityEngine;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    public sealed class FishingOverlay : MonoBehaviour
    {
        private IslandFishingController room;
        private Font font;
        private FishingEquipmentOverlay equipmentView;
        private RevivalOverlay revivalView;
        private GUIStyle title,body,small,button,field,big;
        private string address="127.0.0.1",port="7778";
        private int capacity=2;
        private HudPainter hudPainter;
        private GameplayHud hud;
        private AcquisitionHud acquisitions;
        private bool menuOpen,helpOpen;
        public bool MenuOpen { get=>menuOpen; set { menuOpen=value;if(!value)helpOpen=false; } }
        private float Scale=>HudLayout.Scale(Screen.width,Screen.height);
        public void OpenMenu() { room.CloseShop();room.Equipment?.Close();room.Construction?.Close();helpOpen=false;MenuOpen=true; }
        public void OpenHelp() { OpenMenu();helpOpen=true; }
        public bool PointerOverHud
        {
            get
            {
                var p=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y)/Scale;
                if(room.Incapacitated || MenuOpen || room.Equipment?.InventoryOpen==true || room.ShopOpen)return true;
                if(!room.Playing)return room.InRoom;
                var layout=new HudLayout(Screen.width/Scale,Screen.height/Scale);
                return (room.State?.Tackle!=null && room.Construction?.Open!=true && layout.Quickbar.Contains(p)) || layout.Contains(p,room.State?.Players.Count??1,room.Construction?.Open==true,room.State?.Tackle?.Config.Commissions.Enabled==true,room.State?.Tackle?.Config.Ocean.Enabled==true,room.State!=null && room.State.Finale.Phase!=FinalePhase.Dormant,hud?.ObjectiveReveal??1);
            }
        }
        private void Awake()
        {
            room=GetComponent<IslandFishingController>();
            equipmentView=GetComponent<FishingEquipmentOverlay>();revivalView=GetComponent<RevivalOverlay>();
            font=Font.CreateDynamicFontFromOSFont(new[]{"PingFang SC","Microsoft YaHei","Noto Sans CJK SC","Arial"},24);
        }
        private void Update()
        {
            if(!room.InRoom || !Application.isFocused)return;
            if(Input.GetKeyDown(KeyCode.Escape))
            {if(room.Construction?.Open==true)room.Construction.Close();else if(room.ShopOpen)room.CloseShop();else if(room.Equipment?.InventoryOpen==true)room.Equipment.Close();else MenuOpen=!MenuOpen;}
            else if(Input.GetKeyDown(KeyCode.F1)) { if(helpOpen)MenuOpen=false;else OpenHelp(); }
            else if(Input.GetKeyDown(KeyCode.J) && room.Playing && !MenuOpen && !room.ShopOpen && room.Equipment?.InventoryOpen!=true)hud?.ToggleObjectives();
            else if(Input.GetKeyDown(KeyCode.E) && !MenuOpen)
            {if(room.ShopOpen)room.CloseShop();else if(room.Treasures?.TryOpenNearest()!=true)room.OpenShop();}
        }
        private GUIStyle Style(GUIStyle source,int size,Color color)
        {return new GUIStyle(source){font=font,fontSize=size,normal={textColor=color},wordWrap=true};}
        private static void Panel(Rect rect,Color? tint=null)
        {var old=GUI.color;GUI.color=tint??new Color(.025f,.10f,.14f,.91f);GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}
        private void OnGUI()
        {
            if(room==null)return;
            if(title==null)
            {
                title=Style(GUI.skin.label,25,new Color(1,.96f,.84f));body=Style(GUI.skin.label,17,Color.white);
                small=Style(GUI.skin.label,14,new Color(.7f,.86f,.89f));button=Style(GUI.skin.button,19,Color.white);
                field=Style(GUI.skin.textField,20,Color.white);big=Style(GUI.skin.label,32,new Color(1,.82f,.29f));
            }
            var previous=GUI.matrix;float scale=Scale;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            float w=Screen.width/scale,h=Screen.height/scale;
            if(hudPainter==null){hudPainter=new HudPainter(font);hud=new GameplayHud(room,this,hudPainter);acquisitions=new AcquisitionHud(hudPainter,room.Feedback.Acquisitions);}
            hudPainter.BeginFrame();var layout=new HudLayout(w,h);
            if(room.Playing && !MenuOpen && !room.ShopOpen && room.Equipment?.InventoryOpen!=true){hud.Draw(layout,scale);if(!room.State.Won)room.Treasures?.Draw(scale,small); }
            else hud.Hide();
            if(!room.InRoom)DrawLobby(w,h);
            else if(!room.Playing || MenuOpen)
            {
                if(helpOpen && room.Playing)hud.DrawHelp(layout);
                else
                {
                    bool showRules=room.Playing && room.State!=null && !room.State.RoundFinished;
                    bool showSaves=room.Playing && room.Host!=null;
                    float height=(showRules?310:205)+(showSaves?65:0),x=w/2-240,y=h/2-height/2;hudPainter.Surface(new Rect(x,y,480,height));
                    GUI.Label(new Rect(x+25,y+18,430,70),room.Failure??(room.Playing?"菜单":room.Status),title);
                    GUI.Label(new Rect(x+25,y+97,430,32),room.Restarting?"等待全队同步…":room.Synchronizing?"正在接收房间当前进度…":room.CanReconnect?"可重新连接，返回原席位":room.Host!=null?$"端口 {room.Host.Port}":"等待房主开始",small);
                    if(showRules)
                    {
                        bool enabled=room.State.RoomRules.FriendlyPranksEnabled;
                        GUI.Label(new Rect(x+25,y+137,240,32),"友方整蛊 · "+(enabled?"已开启":"已关闭"),body);
                        if(room.Host!=null && room.LocalSlot==0)
                        {
                            bool previousEnabled=GUI.enabled;GUI.enabled=previousEnabled && !room.RoomRulesPending && room.State.RoomRules.LastRequest<uint.MaxValue;
                            if(GUI.Button(new Rect(x+285,y+134,170,35),room.RoomRulesPending?"同步中…":enabled?"关闭整蛊":"开启整蛊",button))room.RequestFriendlyPranks(!enabled);
                            GUI.enabled=previousEnabled;
                        }
                        else GUI.Label(new Rect(x+285,y+140,170,28),"由房主设置",small);
                        GUI.Label(new Rect(x+25,y+185,430,48),"关闭后不能钩走岸上队友。落水救援始终可用。",small);
                    }
                    if(showSaves)
                    {
                        float saveY=y+height-119;bool enabled=GUI.enabled;GUI.enabled=enabled && room.CanRequestSave;
                        if(GUI.Button(new Rect(x+25,saveY,200,38),room.SavePending?"取消等待保存":"保存",button))
                        {if(room.SavePending)room.CancelSave();else room.RequestSave();}
                        if(GUI.Button(new Rect(x+255,saveY,200,38),"保存并离开",button))room.RequestSave(true);
                        GUI.enabled=enabled;
                    }
                    float actionsY=y+height-57;
                    if(GUI.Button(new Rect(x+25,actionsY,200,38),room.Host!=null?"不保存离开":"离开房间",button))room.LeaveRoom();
                    if(room.Playing && GUI.Button(new Rect(x+255,actionsY,200,38),"继续",button))MenuOpen=false;
                    else if(room.CanStartRoom && GUI.Button(new Rect(x+255,actionsY,200,38),"现在开始",button))room.StartRoomNow();
                    else if(room.CanReconnect && GUI.Button(new Rect(x+255,actionsY,200,38),"重新连接",button))room.Reconnect();
                }
            }
            else if(room.ShopOpen && room.State?.Tackle==null)DrawShop(w,h);
            equipmentView?.Draw();revivalView?.Draw(w,h);
            if(room.Playing && !MenuOpen && !room.State.Won)acquisitions.Draw(room,layout,scale,!room.ShopOpen && room.Equipment?.InventoryOpen!=true);
            if(!string.IsNullOrEmpty(room.SaveMessage))GUI.Label(new Rect(24,h-42,w-48,34),room.SaveMessage,small);
            hudPainter.Tooltip(w,h);GUI.matrix=previous;
        }
        private void DrawShop(float w,float h)
        {
            if(!room.TryGetLocalPlayer(out var player))return;
            float x=w/2-320,y=Mathf.Max(138,h/2-208);
            Panel(new Rect(x,y,640,416),new Color(.025f,.09f,.12f,.98f));
            GUI.Label(new Rect(x+26,y+18,420,38),"小岛钩索商店",title);
            if(GUI.Button(new Rect(x+502,y+18,112,34),"关闭 E",button))room.CloseShop();
            GUI.Label(new Rect(x+26,y+61,588,45),$"全队可用  {room.State.Coins:N0} G",big);
            GUI.Label(new Rect(x+26,y+109,588,30),$"为 P{room.LocalSlot+1} 升级 · 消耗全队金币，提升自己的钩索",body);
            DrawUpgradeCard(new Rect(x+24,y+152,290,179),player,HookUpgrade.Range);
            DrawUpgradeCard(new Rect(x+326,y+152,290,179),player,HookUpgrade.Speed);
            string message=room.ShopMessage;
            if(string.IsNullOrEmpty(message))message=player.Phase!=HookPhase.Ready?"钩索还在外面，收回后即可升级":"升级立即生效 · 两项升级均可提升到 Lv.3";
            GUI.Label(new Rect(x+26,y+344,588,33),message,body);
            GUI.Label(new Rect(x+26,y+383,588,26),"购物时免疫队友拉拽 · 好友仍可捕鱼 · E / Esc 返回",small);
        }
        private void DrawUpgradeCard(Rect rect,Fisher player,HookUpgrade upgrade)
        {
            bool range=upgrade==HookUpgrade.Range;
            int level=range?player.RangeLevel:player.SpeedLevel;
            bool max=level>=HookUpgrades.MaxLevel;
            Panel(rect,new Color(.08f,.22f,.26f,.95f));
            GUI.Label(new Rect(rect.x+16,rect.y+11,258,35),(range?"延长钩索":"加速出钩")+$"   Lv.{level}",title);
            int current=range?HookUpgrades.Range(level)/1000:HookUpgrades.SpeedPerTick(level)*30/1000;
            int next=max?current:range?HookUpgrades.Range(level+1)/1000:HookUpgrades.SpeedPerTick(level+1)*30/1000;
            string unit=range?"米":"米/秒";
            GUI.Label(new Rect(rect.x+16,rect.y+53,258,35),max?$"{current} {unit} · 已满级":$"{current} → {next} {unit}",title);
            GUI.Label(new Rect(rect.x+16,rect.y+93,258,28),range?"Lv.1 中海鱼 · Lv.2 远海金鱼":"缩短飞行时间，更容易截住游鱼",small);
            var check=HookUpgrades.CheckPurchase(player,room.State.Coins,upgrade);
            GUI.enabled=!room.PurchasePending && check==PurchaseResult.Success;
            string label=max?"已满级":$"升级 · {HookUpgrades.Cost(level)} G";
            if(check==PurchaseResult.InsufficientCoins)label=$"需要 {HookUpgrades.Cost(level)} G · 金币不足";
            if(GUI.Button(new Rect(rect.x+14,rect.y+131,262,36),label,button))room.RequestUpgrade(upgrade);
            GUI.enabled=true;
        }
        private void DrawLobby(float w,float h)
        {
            bool saved=room.HasManualSave || room.HasAutoSave;
            float height=saved?432:362,x=w/2-264,y=Mathf.Max(70,h/2-height/2);
            Panel(new Rect(x,y,528,height));
            GUI.Label(new Rect(x+28,y+18,472,38),"把鱼钩上岸，和好友分享收获",title);
            GUI.Label(new Rect(x+28,y+61,472,27),room.Failure??"1–4 人合作捕鱼 · 本机 / 局域网联机",small);
            GUI.Label(new Rect(x+28,y+106,104,30),"房主地址",body);
            address=GUI.TextField(new Rect(x+136,y+102,230,35),address,45,field);
            port=GUI.TextField(new Rect(x+380,y+102,120,35),port,5,field);
            GUI.Label(new Rect(x+28,y+156,104,30),"房间人数",body);
            for(int i=2;i<=4;i++)
            {
                var old=GUI.backgroundColor;GUI.backgroundColor=capacity==i?new Color(.2f,.75f,.7f):Color.white;
                if(GUI.Button(new Rect(x+136+(i-2)*123,y+151,112,36),i+" 人",button))capacity=i;GUI.backgroundColor=old;
            }
            bool valid=int.TryParse(port,out int number) && number>0 && number<=65535;
            GUI.enabled=valid;
            if(GUI.Button(new Rect(x+28,y+207,224,46),"创建联机房间",button))room.HostRoom(number,capacity);
            if(GUI.Button(new Rect(x+276,y+207,224,46),"加入好友",button))room.JoinRoom(address,number);
            GUI.enabled=true;
            if(GUI.Button(new Rect(x+28,y+267,224,42),"先自己玩",button))room.HostRoom(0,1);
            GUI.Label(new Rect(x+276,y+268,224,66),"同机填 127.0.0.1\n不同电脑填房主局域网 IP",small);
            GUI.Label(new Rect(x+28,y+322,472,26),"人数到齐自动开局，也可提前开始。好友可中途加入空位。",small);
            if(saved)
            {
                bool enabled=GUI.enabled;
                GUI.enabled=enabled && valid && room.HasManualSave;
                if(GUI.Button(new Rect(x+28,y+369,224,42),"继续 · 手动存档",button))room.ContinueGame(number);
                GUI.enabled=enabled && valid && room.HasAutoSave;
                if(GUI.Button(new Rect(x+276,y+369,224,42),"继续 · 自动存档",button))room.ContinueGame(number,true);
                GUI.enabled=enabled;
            }
        }
        private void OnDestroy(){hudPainter?.Dispose();if(font!=null)Destroy(font);}
    }
}
