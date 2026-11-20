using Advanture.Tactical.Network;
using Advanture.Tactical.Simulation;
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
        private float hitUntil, damageUntil, reticlePulse;
        private int seenShots;
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
            if (!room.LocalPlayer.HasValue) { lastHits = 0; lastHealth = 100; seenShots = 0; reticlePulse = 0; return; }
            int shots = room.LocalWeapon != null ? room.LocalWeapon.PresentedShots : 0;
            reticlePulse = shots > seenShots ? 1 : reticlePulse * Mathf.Exp(-14 * Time.unscaledDeltaTime);
            seenShots = shots;
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
            Panel(new Rect(24,24,530,88)); GUI.Label(new Rect(42,34,490,33), room.MapTitle, title);
            GUI.Label(new Rect(42,72,490,28), room.StatusText, body);
            if (!room.InRoom)
            {
                float x = width / 2 - 230, y = height / 2 - 125; Panel(new Rect(x,y,460,room.AllowSolo ? 345 : 290));
                GUI.Label(new Rect(x+24,y+20,415,30), room.AllowSolo ? "进入要塞基地" : "和好友一起进入庭院", title);
                GUI.Label(new Rect(x+24,y+65,110,28), "房主地址", body); address = GUI.TextField(new Rect(x+130,y+62,300,34), address, 45, field);
                GUI.Label(new Rect(x+24,y+110,110,28), "端口", body); port = GUI.TextField(new Rect(x+130,y+107,300,34), port, 5, field);
                bool valid = int.TryParse(port, out int number) && number > 0 && number <= 65535; GUI.enabled = valid;
                if (GUI.Button(new Rect(x+24,y+162,195,46), "创建双人房间", button)) room.HostRoom(number);
                if (GUI.Button(new Rect(x+235,y+162,195,46), "加入好友房间", button)) room.JoinRoom(address, number);
                GUI.enabled = true; GUI.Label(new Rect(x+24,y+228,415,48), "本机测试使用 127.0.0.1，两人到齐后开局。\n局域网联机填写房主电脑的 IP 地址。", body);
                if (room.AllowSolo && GUI.Button(new Rect(x+24,y+285,195,40), "先自己玩", button)) room.PlaySolo();
                string destination = room.AllowSolo ? "DrownedBellTown" : "FortifiedCompound";
                if (Application.CanStreamedLevelBeLoaded(destination) && GUI.Button(new Rect(24,128,200,38), room.AllowSolo ? "返回沉钟镇" : "进入要塞基地", button))
                    UnityEngine.SceneManagement.SceneManager.LoadScene(destination);
            }
            else if (!room.Input.HasControl)
            {
                var panel = ControlPanel(width, height); Panel(panel);
                GUI.Label(new Rect(panel.x+20,panel.y+14,panel.width-40,36), room.Playing ? "点击画面，开始操作" : "等待开局 / 可返回重新连接", title);
                if (GUI.Button(LeaveButtonRect(panel), "离开房间", button)) room.LeaveRoom();
            }
            else
            {
                DrawReticle(new Vector2(width / 2, height / 2));
            }
            DrawCombatHud(width,height);
            if (!room.Playing || !room.Input.HasControl || diagnostics)
            {
                Panel(new Rect(24,height-58,850,34));
                GUI.Label(new Rect(38,height-55,825,30), "WASD 移动   空格 跳跃   Shift 加速   左键 连射   R 换弹   Esc 释放鼠标   F3 状态", body);
            }
            if (diagnostics && room.Client?.Session != null)
            {
                var s = room.Client.Session; Panel(new Rect(width-380,24,356,110));
                GUI.Label(new Rect(width-364,35,325,92), "确认帧 " + s.ConfirmedTick + " / 预测帧 " + s.PredictedTick + "\n回滚 " + s.RollbackCount + " / 已冻结 " + room.Client.LastSubmittedTick + "\n30 Hz · 可靠 UDP · 双方状态逐帧核对", body);
            }
            if (room.DiagnosticWarning != null)
                GUI.Label(new Rect(42,112,620,32), "诊断日志保存失败，详情见程序日志。", body);
            GUI.matrix = old;
        }
        private void DrawReticle(Vector2 center)
        {
            var previous = GUI.color;
            float gap = 4 + reticlePulse * 4;
            bool hit = Time.unscaledTime < hitUntil;
            Color ink = hit ? new Color(1, .75f, .37f) : Color.white;
            ReticleStroke(new Rect(center.x-gap-5, center.y-.8f, 5, 1.6f), ink);
            ReticleStroke(new Rect(center.x+gap, center.y-.8f, 5, 1.6f), ink);
            ReticleStroke(new Rect(center.x-.8f, center.y-gap-5, 1.6f, 5), ink);
            ReticleStroke(new Rect(center.x-.8f, center.y+gap, 1.6f, 5), ink);
            if (hit)
            {
                var old = GUI.matrix;
                GUIUtility.RotateAroundPivot(45, center);
                ReticleStroke(new Rect(center.x-13, center.y-.8f, 5, 1.6f), ink);
                ReticleStroke(new Rect(center.x+8, center.y-.8f, 5, 1.6f), ink);
                ReticleStroke(new Rect(center.x-.8f, center.y-13, 1.6f, 5), ink);
                ReticleStroke(new Rect(center.x-.8f, center.y+8, 1.6f, 5), ink);
                GUI.matrix = old;
            }
            GUI.color = previous;
        }

        private static void ReticleStroke(Rect rect, Color ink)
        {
            GUI.color = new Color(.05f, .07f, .08f, .65f);
            GUI.DrawTexture(new Rect(rect.x-1, rect.y-1, rect.width+2, rect.height+2), Texture2D.whiteTexture);
            GUI.color = ink;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        /// <summary>生命、弹药与战绩均读取确认状态，画面不自行推测伤害或修改子弹数。</summary>
        private void DrawCombatHud(float width,float height)
        {
            if(!room.LocalPlayer.HasValue)return;
            var c=room.LocalPlayer.Value.Combat;ulong tick=room.Client.Session.ConfirmedTick;
            // Keep the lower-right silhouette and support hand clear in the first-person camera.
            bool hasHelp = !room.Input.HasControl || diagnostics;
            float x = 24, y = height - (hasHelp ? 158 : 114);
            Panel(new Rect(x, y, 286, 90));
            GUI.Label(new Rect(x+12, y+5, 104, 33), "AK-47", title);
            GUI.Label(new Rect(x+112, y+5, 143, 33), c.Ammo.ToString("00")+" / "+c.Reserve, title);
            bool reloading = c.ReloadUntilTick > tick;
            float remaining = reloading ? (c.ReloadUntilTick-tick) / (float)TacticalClient.TickRate : 0;
            string status = reloading ? "换弹  "+remaining.ToString("0.0")+" 秒" : c.Ammo == 0 ? "弹匣已空 · R 换弹" : "全自动";
            GUI.Label(new Rect(x+12, y+40, 122, 26), status, body);
            GUI.Label(new Rect(x+125, y+40, 153, 26), "生命 "+c.Health+"   K/D "+c.Kills+"/"+c.Deaths, body);
            var previous = GUI.color;
            GUI.color = new Color(.26f,.29f,.30f);
            GUI.DrawTexture(new Rect(x+12, y+73, 262, 3), Texture2D.whiteTexture);
            float fraction = reloading ? 1 - remaining * 1000 / CombatState.ReloadDurationMilliseconds : c.Health / 100f;
            GUI.color = reloading ? new Color(.94f,.71f,.38f) : c.Health>25 ? new Color(.3f,.8f,.63f) : new Color(1,.3f,.2f);
            GUI.DrawTexture(new Rect(x+12, y+73, 262*Mathf.Clamp01(fraction), 3), Texture2D.whiteTexture);
            GUI.color = previous;
            if(Time.unscaledTime<damageUntil)
            {
                GUI.color=new Color(1,.12f,.05f,.35f);GUI.DrawTexture(new Rect(0,0,width,10),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(0,height-10,width,10),Texture2D.whiteTexture);GUI.color=previous;
            }
            if(!c.IsAlive)
            {
                Panel(new Rect(width/2-210,height/2-72,420,115));GUI.Label(new Rect(width/2-175,height/2-60,370,40),"你已阵亡",title);
                GUI.Label(new Rect(width/2-175,height/2-16,370,40),Mathf.Max(0,((long)c.RespawnAtTick-(long)tick)/(float)TacticalClient.TickRate).ToString("0.0")+" 秒后自动重生",body);
            }
        }
        private void OnDestroy() { if (font != null) Destroy(font); }
    }
}
