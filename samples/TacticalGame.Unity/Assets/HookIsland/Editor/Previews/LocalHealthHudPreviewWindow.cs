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
        private HealingChargesHud healing;

        [MenuItem("Tools/Hook Island/Preview Local Health HUD")]
        public static void Open()
        {
            var window=GetWindow<LocalHealthHudPreviewWindow>();
            window.titleContent=new GUIContent("本地生命 HUD");
            window.minSize=new Vector2(820,380);window.Show();
        }
        private void OnEnable()
        {
            font=Font.CreateDynamicFontFromOSFont(new[]{"PingFang SC","Microsoft YaHei","Arial"},24);
            painter=new HudPainter(font);hud=new LocalHealthHud(painter);healing=new HealingChargesHud(painter);
            EditorApplication.update+=Repaint;
        }
        private void OnDisable()
        {EditorApplication.update-=Repaint;painter?.Dispose();if(font!=null)DestroyImmediate(font);}
        private void OnGUI()
        {
            HudPainter.Fill(new Rect(0,0,position.width,position.height),new Color(.045f,.12f,.15f));
            painter.Text(new Rect(24,16,760,30),"生命与医疗罐 · 3 / 2 / 1 / 0 次 · 填充表示剩余次数",18,HudPainter.White,TextAnchor.MiddleLeft,true);
            int[] values={100,64,22,0};
            float now=(float)EditorApplication.timeSinceStartup;
            var layout=new HudLayout(1280,720);
            for (int index=0;index<values.Length;index++)
            {
                // 正式角色通过 Health == 0 推导倒地；预览沿用该规则，不另设可写的倒地标志。
                var player=new Fisher{Slot=(ushort)index,Vitals=new CharacterVitals{Health=values[index]},HealingCharges=3-index};
                float fraction=values[index]/100f;
                // 第二张展示 40% → 64% 的绿色治疗段，第三张展示受伤红色拖尾。
                var frame=index==1?new HealthBarFeedback.Frame(.4f,fraction,fraction,.4f,fraction):
                    new HealthBarFeedback.Frame(index==2?.42f:fraction,fraction,fraction);
                float x=24+index%2*400,y=62+index/2*152;
                // 复用正式布局的尺寸和相对位置，防止预览仍显示调整前的血瓶大小。
                var canisterRect=layout.LocalHealing;var healthRect=layout.LocalHealth;
                var offset=new Vector2(x-canisterRect.x,y-Mathf.Min(canisterRect.y,healthRect.y));
                canisterRect.position+=offset;healthRect.position+=offset;
                healing.Draw(canisterRect,player,new HealingConfig());
                hud.Draw(healthRect,player,100,frame,now);
            }
        }
    }
}
