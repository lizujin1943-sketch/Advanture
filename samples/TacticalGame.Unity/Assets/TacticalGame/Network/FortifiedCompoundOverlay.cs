using Advanture.HookIsland;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Advanture.TacticalGame.NetworkView
{
    [RequireComponent(typeof(NetworkYardController))]
    public sealed class FortifiedCompoundOverlay : MonoBehaviour
    {
        public enum Panel { Game, Menu, Help, Equipment }
        public Panel ActivePanel { get; private set; }
        public bool BlocksPlay => ActivePanel!=Panel.Game;
        private readonly CoopLobbyForm lobby=new CoopLobbyForm(7777,4);
        private NetworkYardController room;
        private HudPainter ui;
        private CompoundCombatHud combat;
        private Font font;
        private GUIStyle title,body,small,button,field;
        private bool backpack, diagnostics;

        private void Awake()
        {
            room=GetComponent<NetworkYardController>();
            font=Font.CreateDynamicFontFromOSFont(new[]{"PingFang SC","Microsoft YaHei","Noto Sans CJK SC","Arial"},24);
            ui=new HudPainter(font);combat=new CompoundCombatHud(room,ui);
        }

        // 先处理面板，再由同一帧的输入采样决定是否锁定鼠标或提交战斗按键。
        public void CaptureShortcuts()
        {
            if(!room.Playing || !Application.isFocused)return;
            var keyboard=Keyboard.current;
            if(keyboard==null)return;
            if(keyboard.f3Key.wasPressedThisFrame)diagnostics=!diagnostics;
            bool alive=room.LocalPlayer?.Combat.IsAlive==true;
            if(!alive && ActivePanel==Panel.Equipment)ActivePanel=Panel.Game;
            if(keyboard.escapeKey.wasPressedThisFrame)ActivePanel=BlocksPlay?Panel.Game:Panel.Menu;
            else if(keyboard.f1Key.wasPressedThisFrame)ActivePanel=ActivePanel==Panel.Help?Panel.Game:Panel.Help;
            else if(alive && (keyboard.tabKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame))
                ActivePanel=ActivePanel==Panel.Equipment?Panel.Game:Panel.Equipment;
        }

        public void ResetSession(){ActivePanel=Panel.Game;backpack=diagnostics=false;combat?.Reset();}

        private GUIStyle Style(GUIStyle basis,int size,Color color)=>new GUIStyle(basis)
        {font=font,fontSize=size,wordWrap=true,normal={textColor=color}};

        private void OnGUI()
        {
            if(title==null)
            {
                title=Style(GUI.skin.label,25,new Color(1,.96f,.84f));
                body=Style(GUI.skin.label,17,Color.white);
                small=Style(GUI.skin.label,14,new Color(.7f,.86f,.89f));
                button=Style(GUI.skin.button,19,Color.white);field=Style(GUI.skin.textField,20,Color.white);
            }
            float scale=HudLayout.Scale(Screen.width,Screen.height);
            var layout=new HudLayout(Screen.width/scale,Screen.height/scale);
            var previous=GUI.matrix;
            GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            try
            {
                ui.BeginFrame();
                if(!room.InRoom)DrawLobby(layout);
                else if(!room.Playing || ActivePanel==Panel.Menu)DrawMenu(layout);
                else if(ActivePanel==Panel.Help)DrawHelp(layout);
                else if(room.LocalPlayer.HasValue)
                {
                    if(ActivePanel==Panel.Equipment)DrawEquipment(layout);
                    else combat.Draw(layout,scale);
                }
                if(diagnostics && room.Playing)DrawDiagnostics(layout);
                if(room.DiagnosticWarning!=null)ui.Text(new Rect(24,12,475,26),"诊断日志保存失败，详情见程序日志。",14,HudPainter.Gold);
                if(BlocksPlay)ui.Tooltip(layout.Width,layout.Height);
            }
            finally {GUI.matrix=previous;}
        }

        private void DrawLobby(HudLayout layout)
        {
            bool town=Application.CanStreamedLevelBeLoaded("DrownedBellTown");
            float height=town?414:362;
            var rect=new Rect((layout.Width-528)/2,Mathf.Max(20,(layout.Height-height)/2),528,height);
            lobby.Draw(rect,room.MapTitle,room.Failure??"1–4 人合作防守 · 本机 / 局域网联机",
                "人数到齐自动开局。至少两人就绪后，房主也可提前开始。",title,body,small,button,field,room.HostRoom,room.JoinRoom,room.PlaySolo);
            if(town && GUI.Button(new Rect(rect.x+28,rect.y+356,472,40),"返回沉钟镇",button))
                SceneManager.LoadScene("DrownedBellTown");
        }

        private void DrawMenu(HudLayout layout)
        {
            bool playing=room.Playing;
            var rect=new Rect(layout.Width/2-240,layout.Height/2-105,480,210);
            ui.Surface(rect);
            GUI.Label(new Rect(rect.x+25,rect.y+18,430,62),room.Failure??(playing?"菜单":room.StatusText),title);
            string info=room.Host!=null?$"端口 {room.Host.Port}":"等待房主开始";
            GUI.Label(new Rect(rect.x+25,rect.y+80,430,31),playing?room.MapTitle:info,small);
            if(playing && GUI.Button(new Rect(rect.x+255,rect.y+83,200,38),"操作指南  F1",button))ActivePanel=Panel.Help;
            if(GUI.Button(new Rect(rect.x+25,rect.yMax-57,200,38),"离开房间",button)){room.LeaveRoom();return;}
            var action=new Rect(rect.x+255,rect.yMax-57,200,38);
            if(playing)
            {
                if(GUI.Button(action,"继续",button))ActivePanel=Panel.Game;
            }
            else if(room.Host!=null && room.Failure==null)
            {
                bool enabled=GUI.enabled;GUI.enabled=enabled && room.Host.CanStart;
                if(GUI.Button(action,"现在开始",button))room.Host.TryStart();
                GUI.enabled=enabled;
            }
        }

        private static readonly (string key,string hint)[] Controls={
            ("WASD","按视角方向移动"),("鼠标","转向并瞄准画面中央"),("左键","开火 · 按住连射"),
            ("R","换弹"),("E","在武器库补满备弹"),("空格","跳跃"),("Shift","加速移动"),
            ("Tab / I","角色与背包"),("Esc / F1","菜单 / 操作指南"),("F3","联机状态")
        };
        private void DrawHelp(HudLayout layout)
        {
            var rect=layout.Help;ui.Surface(rect);
            ui.Text(new Rect(rect.x+26,rect.y+18,300,32),"操作指南",20,HudPainter.White,TextAnchor.MiddleLeft,true);
            if(ui.IconButton(new Rect(rect.xMax-48,rect.y+13,32,32),HudIcon.Close,"关闭 · Esc"))ActivePanel=Panel.Game;
            for(int i=0;i<Controls.Length;i++)ui.HelpRow(rect,i,Controls[i].key,Controls[i].hint);
        }

        private void DrawEquipment(HudLayout layout)
        {
            var rect=new Rect(layout.Width/2-320,layout.Height/2-149,640,298);
            ui.Surface(rect,new Color(.025f,.095f,.12f,.98f));
            HudPainter.Fill(new Rect(rect.x,rect.y,rect.width,3),HudPainter.Mint);
            ui.Text(new Rect(rect.x+24,rect.y+15,204,36),backpack?"我的背包":"角色与装备",23,HudPainter.Gold);
            bool enabled=GUI.enabled;GUI.enabled=enabled && backpack;
            if(GUI.Button(new Rect(rect.x+260,rect.y+18,126,32),"角色 · 属性",button))backpack=false;
            GUI.enabled=enabled && !backpack;
            if(GUI.Button(new Rect(rect.x+395,rect.y+18,126,32),"背包 · 物资",button))backpack=true;
            GUI.enabled=enabled;
            if(GUI.Button(new Rect(rect.x+535,rect.y+18,80,32),"关闭",button))ActivePanel=Panel.Game;
            combat.DrawEquipment(rect,room.LocalPlayer.Value,backpack);
            ui.Text(new Rect(rect.x+24,rect.yMax-44,592,26),"Tab / I 关闭面板",14,HudPainter.Muted);
        }

        private void DrawDiagnostics(HudLayout layout)
        {
            var session=room.Client.Session;
            var rect=new Rect(layout.Width-300,24,276,70);ui.Surface(rect);
            ui.Text(new Rect(rect.x+12,rect.y+8,250,24),$"确认帧 {session.ConfirmedTick} / 预测帧 {session.PredictedTick}");
            ui.Text(new Rect(rect.x+12,rect.y+35,250,24),$"回滚 {session.RollbackCount} · 30 Hz",14,HudPainter.Muted);
        }
        private void OnDestroy(){ui?.Dispose();if(font!=null)Destroy(font);}
    }
}
