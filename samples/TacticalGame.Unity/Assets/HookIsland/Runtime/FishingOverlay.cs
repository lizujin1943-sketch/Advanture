using UnityEngine;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    public sealed class FishingOverlay : MonoBehaviour
    {
        private IslandFishingController room;
        private Font font;
        private GUIStyle title,body,small,button,field,big;
        private string address="127.0.0.1",port="7778";
        private int capacity=2;
        public bool MenuOpen { get; set; }
        private float Scale=>Mathf.Clamp(Mathf.Min(Screen.width/1280f,Screen.height/720f),.45f,2);
        public bool PointerOverHud
        {
            get
            {
                var p=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y)/Scale;
                float width=Screen.width/Scale;
                return MenuOpen || new Rect(20,20,365,82).Contains(p) ||
                    new Rect(width-262,20,242,280).Contains(p) ||
                    new Rect(width/2-50,23,100,35).Contains(p) || p.y>Screen.height/Scale-72;
            }
        }
        private void Awake()
        {
            room=GetComponent<IslandFishingController>();
            font=Font.CreateDynamicFontFromOSFont(new[]{"PingFang SC","Microsoft YaHei","Noto Sans CJK SC","Arial"},24);
        }
        private void Update()
        {if(room.InRoom && Input.GetKeyDown(KeyCode.Escape))MenuOpen=!MenuOpen;}
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
            Panel(new Rect(20,20,365,82));GUI.Label(new Rect(38,27,340,36),"钩钩小岛  /  HOOK ISLAND",title);
            GUI.Label(new Rect(38,66,340,26),room.Playing?$"{room.State?.Players.Count??0} 人捕鱼 · 你是 P{room.LocalSlot+1}": "一起出钩，把收获带上岸",small);
            if(room.State!=null)
            {
                Panel(new Rect(w-262,20,242,104));GUI.Label(new Rect(w-244,28,208,25),"全队金币",body);
                GUI.Label(new Rect(w-244,56,208,48),room.State.Coins.ToString("N0")+"  G",big);
                float y=140;
                foreach(var p in room.State.Players)
                {
                    Panel(new Rect(w-226,y,206,32),new Color(.025f,.10f,.14f,.75f));
                    var old=GUI.color;GUI.color=FishingPresentation.PlayerColors[p.Slot];GUI.DrawTexture(new Rect(w-214,y+10,10,10),Texture2D.whiteTexture);GUI.color=old;
                    GUI.Label(new Rect(w-197,y+3,175,28),$"P{p.Slot+1}  {p.Catches} 条  ·  +{p.Coins} G",small);y+=37;
                }
                DrawWorldLabels(scale,h);
            }
            if(room.InRoom && GUI.Button(new Rect(w/2-50,23,100,35),MenuOpen?"继续":"菜单 Esc",button))MenuOpen=!MenuOpen;
            if(!room.InRoom)DrawLobby(w,h);
            else if(!room.Playing || MenuOpen)
            {
                float x=w/2-240,y=h/2-100;Panel(new Rect(x,y,480,205));
                GUI.Label(new Rect(x+25,y+18,430,70),room.Failure??(room.Playing?"休息一下 · 好友仍可继续捕鱼":room.Status),title);
                GUI.Label(new Rect(x+25,y+97,430,32),room.Host!=null?$"端口 {room.Host.Port}  ·  同局域网好友使用房主 IP 加入":"连接房主后，人数到齐自动开始",small);
                if(GUI.Button(new Rect(x+25,y+148,200,38),"离开房间",button))room.LeaveRoom();
                if(room.Playing && GUI.Button(new Rect(x+255,y+148,200,38),"继续捕鱼",button))MenuOpen=false;
            }
            Panel(new Rect(20,h-64,w-40,44));
            GUI.Label(new Rect(36,h-57,w-72,32),"WASD 移动     鼠标瞄准     左键出钩     右键收回空钩     滚轮缩放     Esc 菜单",body);
            if(room.Playing && !MenuOpen)
            {
                string hint="等鱼跃起再出钩 · 拉回岸上自动获得金币";
                if(room.State!=null)foreach(var p in room.State.Players)if(p.Slot==room.LocalSlot)
                    hint=p.FishId>=0?"钩中了！正在把鱼拉回岸上…":p.Phase==HookPhase.Flying?"出钩中 · 右键可提前收回":p.Phase==HookPhase.Returning?"收钩中…":hint;
                var centered=new GUIStyle(body){alignment=TextAnchor.MiddleCenter};
                GUI.Label(new Rect(w/2-300,h-105,600,32),hint,centered);
            }
            GUI.matrix=previous;
        }
        private void DrawLobby(float w,float h)
        {
            float x=w/2-264,y=Mathf.Max(124,h/2-181);
            Panel(new Rect(x,y,528,362));
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
            GUI.Label(new Rect(x+28,y+322,472,26),"人数到齐自动开局。开始后不能中途加入。",small);
        }
        private void DrawWorldLabels(float scale,float height)
        {
            foreach(var p in room.State.Players)
            {
                var point=room.ViewCamera.WorldToScreenPoint(room.Presentation.PlayerPosition(p.Slot)+Vector3.up*2.2f);
                if(point.z<=0)continue;
                var style=new GUIStyle(small){alignment=TextAnchor.MiddleCenter,normal={textColor=FishingPresentation.PlayerColors[p.Slot]}};
                GUI.Label(new Rect(point.x/scale-45,height-point.y/scale-12,90,27),p.Slot==room.LocalSlot?$"P{p.Slot+1} · 你":$"P{p.Slot+1}",style);
            }
            foreach(var pop in room.Presentation.Popups)
            {
                var point=room.ViewCamera.WorldToScreenPoint(pop.Position+Vector3.up*(1.5f-(pop.Until-Time.unscaledTime)));
                GUI.Label(new Rect(point.x/scale-45,height-point.y/scale-20,130,44),"+"+pop.Value+" G",big);
            }
        }
        private void OnDestroy(){if(font!=null)Destroy(font);}
    }
}
