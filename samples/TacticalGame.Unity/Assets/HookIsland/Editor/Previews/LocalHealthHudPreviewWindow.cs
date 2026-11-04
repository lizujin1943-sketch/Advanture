using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Editor
{
    /// <summary>显式打开的编辑器预览，只向正式绘制类传入示例值，不连接房间、不修改角色生命。</summary>
    public sealed class LocalHealthHudPreviewWindow : EditorWindow
    {
        private Font font;
        private HudPainter painter;
        private LocalHealthHud hud;

        [MenuItem("Tools/Hook Island/Preview Local Health HUD")]
        public static void Open()
        {
            var window=GetWindow<LocalHealthHudPreviewWindow>();
            window.titleContent=new GUIContent("本地生命 HUD");
            window.minSize=new Vector2(740,340);window.Show();
        }
        private void OnEnable()
        {
            font=Font.CreateDynamicFontFromOSFont(new[]{"PingFang SC","Microsoft YaHei","Arial"},24);
            painter=new HudPainter(font);hud=new LocalHealthHud(painter);
            EditorApplication.update+=Repaint;
        }
        private void OnDisable()
        {EditorApplication.update-=Repaint;painter?.Dispose();if(font!=null)DestroyImmediate(font);}
        private void OnGUI()
        {
            HudPainter.Fill(new Rect(0,0,position.width,position.height),new Color(.045f,.12f,.15f));
            painter.Text(new Rect(24,16,680,30),"本地生命 HUD · 满血 / 受伤 / 低生命 / 倒地",18,HudPainter.White,TextAnchor.MiddleLeft,true);
            int[] values={100,64,22,0};
            float now=(float)EditorApplication.timeSinceStartup;
            for (int index=0;index<values.Length;index++)
            {
                // 正式角色通过 Health == 0 推导倒地；预览沿用该规则，不另设可写的倒地标志。
                var player=new Fisher{Slot=(ushort)index,Vitals=new CharacterVitals{Health=values[index]}};
                float fraction=values[index]/100f;
                // 第二张保留一小段受伤拖尾，便于静态检查白色生命与红色损失的区别。
                var frame=new HealthBarFeedback.Frame(index==1?.83f:fraction,fraction,fraction);
                hud.Draw(new Rect(26+index%2*360,70+index/2*126,320,96),player,100,frame,now);
            }
        }
    }
}
