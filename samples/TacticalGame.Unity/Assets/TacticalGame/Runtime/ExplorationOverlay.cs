using UnityEngine;

namespace Advanture.TacticalGame
{
    /// <summary>试玩所需的简洁操作提示和准星。仅展示 UI，不读取或修改角色运动。</summary>
    [RequireComponent(typeof(FirstPersonInput))]
    public sealed class ExplorationOverlay : MonoBehaviour
    {
        private FirstPersonInput input;
        private Font font;
        private GUIStyle title, body, centeredTitle, centeredBody;

        private void Awake()
        {
            input = GetComponent<FirstPersonInput>();
            // 系统字体按顺序回退，兼顾 macOS 和 Windows 的中文显示。
            font = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Microsoft YaHei", "Arial" }, 20);
        }

        private GUIStyle Style(int size, FontStyle weight, TextAnchor align)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = font, fontSize = size, fontStyle = weight, alignment = align,
                normal = { textColor = new Color(.94f,.95f,.92f) }
            };
        }

        private static void Panel(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void OnGUI()
        {
            if (title == null)
            {
                title = Style(23, FontStyle.Bold, TextAnchor.UpperLeft);
                body = Style(15, FontStyle.Normal, TextAnchor.MiddleLeft);
                centeredTitle = Style(25, FontStyle.Bold, TextAnchor.MiddleCenter);
                centeredBody = Style(16, FontStyle.Normal, TextAnchor.MiddleCenter);
            }
            float scale = Mathf.Clamp(Screen.width / 1280f, .65f, 1.6f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale, height = Screen.height / scale;
            Panel(new Rect(24,24,200,74), new Color(.08f,.14f,.17f,.80f));
            Panel(new Rect(24,24,4,74), new Color(.83f,.67f,.34f));
            GUI.Label(new Rect(42,34,172,32), "七号转运站", title);
            GUI.Label(new Rect(42,65,172,23), "YARD 07 · 自由探索", body);
            Panel(new Rect(24,height-60,565,36), new Color(.08f,.14f,.17f,.78f));
            GUI.Label(new Rect(38,height-59,540,34), "WASD 移动   Shift 加速   鼠标 转向   R 重置   Esc 释放鼠标", body);
            if (input.HasControl)
            {
                var c = new Color(1,1,1,.85f);
                float x=width/2, y=height/2;
                Panel(new Rect(x-8,y-1,5,2),c);
                Panel(new Rect(x+3,y-1,5,2),c);
                Panel(new Rect(x-1,y-8,2,5),c);
                Panel(new Rect(x-1,y+3,2,5),c);
            }
            else
            {
                Panel(new Rect(width/2-220,height/2-60,440,120),new Color(.08f,.14f,.17f,.88f));
                GUI.Label(new Rect(width/2-210,height/2-46,420,48),"点击画面开始探索",centeredTitle);
                GUI.Label(new Rect(width/2-210,height/2+3,420,36),"鼠标控制视角 · 按 Esc 可随时释放鼠标",centeredBody);
            }
            GUI.matrix = previous;
        }

        private void OnDestroy()
        {
            if (font != null) Destroy(font);
        }
    }
}
