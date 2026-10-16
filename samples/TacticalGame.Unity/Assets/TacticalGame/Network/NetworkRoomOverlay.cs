using UnityEngine;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>双人试玩菜单和操作提示；联网诊断仅在按 F3 时显示，默认界面保留游戏操作。</summary>
    [RequireComponent(typeof(NetworkYardController))]
    public sealed class NetworkRoomOverlay : MonoBehaviour
    {
        private NetworkYardController room;
        private Font font;
        private GUIStyle title, body, button, field;
        private string address = "127.0.0.1", port = "7777";
        private bool diagnostics;
        private uint lastHits;
        private int lastHealth=100;
        private float hitUntil,damageUntil;
        // Input System 的 Update 先于 IMGUI。按钮区域要先阻止捕获鼠标，否则点击会隐藏按钮。
        public bool PointerOverLeaveButton
        {
            get
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (room == null || !room.InRoom || mouse == null) return false;
                float scale = Mathf.Clamp(Screen.width / 1280f, .65f, 1.6f);
                var point = mouse.position.ReadValue();
                point = new Vector2(point.x / scale, (Screen.height - point.y) / scale);
                return LeaveButtonRect(ControlPanel(Screen.width / scale, Screen.height / scale)).Contains(point);
            }
        }
        // 开局后把未捕获鼠标的提示放到右上方，避免遮住另一名玩家；绘制和点击检测共用坐标。
        private Rect ControlPanel(float width, float height) => room.Playing
            ? new Rect(width - 434, 154, 410, 110) : new Rect(width / 2 - 235, height / 2 - 50, 470, 110);
        private static Rect LeaveButtonRect(Rect panel) => new Rect(panel.center.x - 90, panel.y + 60, 180, 36);
        private void Awake()
        { room = GetComponent<NetworkYardController>(); font = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Microsoft YaHei", "Arial" }, 20); }
        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current?.f3Key.wasPressedThisFrame == true) diagnostics = !diagnostics;
            if(!room.LocalPlayer.HasValue){lastHits=0;lastHealth=100;return;}
            var combat=room.LocalPlayer.Value.Combat;
            if(combat.Hits>lastHits)hitUntil=Time.unscaledTime+.18f;
            if(combat.Health<lastHealth)damageUntil=Time.unscaledTime+.22f;
            lastHits=combat.Hits;lastHealth=combat.Health;
        }
        private GUIStyle Style(GUIStyle basis, int size) => new GUIStyle(basis) { font = font, fontSize = size, normal = { textColor = Color.white } };
        private static void Panel(Rect area)
        { var previous = GUI.color; GUI.color = new Color(.055f,.10f,.14f,.92f); GUI.DrawTexture(area, Texture2D.whiteTexture); GUI.color = previous; }
        private void OnGUI()
        {
#if DEVELOPMENT_BUILD
            if (YardLaunchArguments.Has("-yard-input-trace") && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.KeyDown))
                Debug.Log("YARD_INPUT_TRACE event=" + Event.current.type + " position=" + Event.current.mousePosition + " screen=" + Screen.width + "x" + Screen.height + " focused=" + Application.isFocused);
#endif
            if (title == null) { title = Style(GUI.skin.label, 25); body = Style(GUI.skin.label, 16); button = Style(GUI.skin.button, 18); field = Style(GUI.skin.textField, 19); }
            float scale = Mathf.Clamp(Screen.width / 1280f, .65f, 1.6f); var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1)); float width = Screen.width / scale, height = Screen.height / scale;
            Panel(new Rect(24,24,530,88)); GUI.Label(new Rect(42,34,490,33), "七号转运站 / 双人联机", title);
            GUI.Label(new Rect(42,72,490,28), room.StatusText, body);
            if (!room.InRoom)
            {
                float x = width / 2 - 230, y = height / 2 - 125; Panel(new Rect(x,y,460,290));
                GUI.Label(new Rect(x+24,y+20,415,30), "和好友一起进入庭院", title);
                GUI.Label(new Rect(x+24,y+65,110,28), "房主地址", body); address = GUI.TextField(new Rect(x+130,y+62,300,34), address, 45, field);
                GUI.Label(new Rect(x+24,y+110,110,28), "端口", body); port = GUI.TextField(new Rect(x+130,y+107,300,34), port, 5, field);
                bool valid = int.TryParse(port, out int number) && number > 0 && number <= 65535; GUI.enabled = valid;
                if (GUI.Button(new Rect(x+24,y+162,195,46), "创建双人房间", button)) room.HostRoom(number);
                if (GUI.Button(new Rect(x+235,y+162,195,46), "加入好友房间", button)) room.JoinRoom(address, number);
                GUI.enabled = true; GUI.Label(new Rect(x+24,y+228,415,48), "本机测试使用 127.0.0.1，两人到齐后开局。\n局域网联机填写房主电脑的 IP 地址。", body);
            }
            else if (!room.Input.HasControl)
            {
                var panel = ControlPanel(width, height); Panel(panel);
                GUI.Label(new Rect(panel.x+20,panel.y+14,panel.width-40,36), room.Playing ? "点击画面，开始操作" : "等待开局 / 可返回重新连接", title);
                if (GUI.Button(LeaveButtonRect(panel), "离开房间", button)) room.LeaveRoom();
            }
            else
            {
                var color=GUI.color;GUI.color=Time.unscaledTime<hitUntil?new Color(1,.35f,.16f):Color.white;
                GUI.DrawTexture(new Rect(width/2-14,height/2-1,8,2),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(width/2+6,height/2-1,8,2),Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(width/2-1,height/2-14,2,8),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(width/2-1,height/2+6,2,8),Texture2D.whiteTexture);
                if(Time.unscaledTime<hitUntil)GUI.Label(new Rect(width/2-10,height/2-18,30,36),"×",title);GUI.color=color;
            }
            DrawCombatHud(width,height);
            Panel(new Rect(24,height-58,850,34)); GUI.Label(new Rect(38,height-55,825,30), "WASD 移动   Shift 加速   左键 连射   R 换弹   Esc 释放鼠标   F3 状态", body);
            if (diagnostics && room.Client?.Session != null)
            {
                var s = room.Client.Session; Panel(new Rect(width-380,24,356,110));
                GUI.Label(new Rect(width-364,35,325,92), "确认帧 " + s.ConfirmedTick + " / 预测帧 " + s.PredictedTick + "\n回滚 " + s.RollbackCount + " / 已冻结 " + room.Client.LastSubmittedTick + "\n30 Hz · 可靠 UDP · 双方状态逐帧核对", body);
            }
            if (room.DiagnosticWarning != null)
                GUI.Label(new Rect(42,112,620,32), "诊断日志保存失败，详情见程序日志。", body);
            GUI.matrix = old;
        }
        /// <summary>生命、弹药与战绩均读取确认状态，画面不自行推测伤害或修改子弹数。</summary>
        private void DrawCombatHud(float width,float height)
        {
            if(!room.LocalPlayer.HasValue)return;
            var c=room.LocalPlayer.Value.Combat;ulong tick=room.Client.Session.ConfirmedTick;
            float x=width-280,y=height-210;Panel(new Rect(x,y,256,180));
            GUI.Label(new Rect(x+18,y+12,220,30),"AK-47  /  AUTO",title);
            GUI.Label(new Rect(x+18,y+49,220,30),c.Ammo.ToString("00")+" / "+c.Reserve+" 发",title);
            string status=c.ReloadUntilTick>tick?"换弹中 "+((c.ReloadUntilTick-tick)/30f).ToString("0.0")+" 秒":c.Ammo==0?"弹匣已空 · 按 R 换弹":"左键射击 · R 换弹";
            GUI.Label(new Rect(x+18,y+84,225,26),status,body);
            var previous=GUI.color;GUI.color=new Color(.2f,.25f,.27f);GUI.DrawTexture(new Rect(x+18,y+115,220,7),Texture2D.whiteTexture);
            GUI.color=c.Health>25?new Color(.3f,.8f,.63f):new Color(1,.3f,.2f);GUI.DrawTexture(new Rect(x+18,y+115,220*c.Health/100f,7),Texture2D.whiteTexture);GUI.color=previous;
            GUI.Label(new Rect(x+18,y+132,225,30),"生命 "+c.Health+"    击杀 "+c.Kills+" / 死亡 "+c.Deaths,body);
            if(Time.unscaledTime<damageUntil)
            {
                GUI.color=new Color(1,.12f,.05f,.35f);GUI.DrawTexture(new Rect(0,0,width,10),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(0,height-10,width,10),Texture2D.whiteTexture);GUI.color=previous;
            }
            if(!c.IsAlive)
            {
                Panel(new Rect(width/2-210,height/2-72,420,115));GUI.Label(new Rect(width/2-175,height/2-60,370,40),"你已阵亡",title);
                GUI.Label(new Rect(width/2-175,height/2-16,370,40),Mathf.Max(0,((long)c.RespawnAtTick-(long)tick)/30f).ToString("0.0")+" 秒后自动重生",body);
            }
        }
        private void OnDestroy() { if (font != null) Destroy(font); }
    }
}
