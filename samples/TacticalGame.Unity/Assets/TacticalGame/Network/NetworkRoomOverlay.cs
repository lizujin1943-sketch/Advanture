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
        { if (UnityEngine.InputSystem.Keyboard.current?.f3Key.wasPressedThisFrame == true) diagnostics = !diagnostics; }
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
                GUI.Label(new Rect(panel.x+20,panel.y+14,panel.width-40,36), room.Playing ? "点击画面，开始移动" : "等待开局 / 可返回重新连接", title);
                if (GUI.Button(LeaveButtonRect(panel), "离开房间", button)) room.LeaveRoom();
            }
            else
            { GUI.Label(new Rect(width/2-8,height/2-17,25,35), "+", title); }
            Panel(new Rect(24,height-58,760,34)); GUI.Label(new Rect(38,height-55,735,30), "WASD 移动   Shift 加速   鼠标 转向   R 回到出生点   Esc 释放鼠标   F3 状态", body);
            if (diagnostics && room.Client?.Session != null)
            {
                var s = room.Client.Session; Panel(new Rect(width-380,24,356,110));
                GUI.Label(new Rect(width-364,35,325,92), "确认帧 " + s.ConfirmedTick + " / 预测帧 " + s.PredictedTick + "\n回滚 " + s.RollbackCount + " / 已冻结 " + room.Client.LastSubmittedTick + "\n30 Hz · 可靠 UDP · 双方状态逐帧核对", body);
            }
            if (room.DiagnosticWarning != null)
                GUI.Label(new Rect(42,112,620,32), "诊断日志保存失败，详情见程序日志。", body);
            GUI.matrix = old;
        }
        private void OnDestroy() { if (font != null) Destroy(font); }
    }
}
