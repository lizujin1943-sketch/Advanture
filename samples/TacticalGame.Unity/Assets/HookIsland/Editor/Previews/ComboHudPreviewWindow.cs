using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Explicit editor-only visual inspection. It never reads or changes a running simulation.</summary>
    public sealed class ComboHudPreviewWindow : EditorWindow
    {
        private HudPainter painter;
        private ComboHud hud;
        private ComboLettering lettering;
        private ComboHudAnimation[] animations;
        private Font font;
        private float started;
        private ulong sequence;
        private readonly object previewIdentity=new object();
        [MenuItem("Tools/Hook Island/Preview Combo HUD")]
        public static void Open()
        {var window=GetWindow<ComboHudPreviewWindow>();window.titleContent=new GUIContent("Combo HUD");window.minSize=new Vector2(1040,310);window.Show();}
        private void OnEnable()
        {
            font=Font.CreateDynamicFontFromOSFont(new[]{"Arial","PingFang SC"},24);
            painter=new HudPainter(font);hud=new ComboHud();lettering=new ComboLettering();
            animations=new[]{new ComboHudAnimation(),new ComboHudAnimation(),new ComboHudAnimation(),new ComboHudAnimation()};
            Replay();EditorApplication.update+=Repaint;
        }
        private void OnDisable()
        {EditorApplication.update-=Repaint;painter?.Dispose();if(font!=null)DestroyImmediate(font);}
        private void Replay()
        {
            started=(float)EditorApplication.timeSinceStartup;sequence++;
            var counts=new[]{3,6,10,100};var config=new ComboConfig();
            for(int i=0;i<4;i++)
            {
                animations[i].Observe(previewIdentity,sequence,1,counts[i]-1,config,started);
                animations[i].Observe(previewIdentity,sequence,2,counts[i],config,started);
                animations[i].ConfirmIncrease(counts[i],started);
            }
        }
        private void OnGUI()
        {
            float now=(float)EditorApplication.timeSinceStartup;
            HudPainter.Fill(new Rect(0,0,position.width,position.height),new Color(.045f,.13f,.16f));
            painter.Text(new Rect(28,15,740,30),"COMBO  /  CONFIRMED HARVEST",17,HudPainter.White,TextAnchor.MiddleLeft,true);
            if(GUI.Button(new Rect(position.width-152,17,124,28),"Replay bounce"))Replay();
            for(int i=0;i<4;i++)
            {
                float x=28+i*252;
                hud.DrawFrame(new Rect(x,71,230,118),animations[i].Sample(now));
            }
            painter.Text(new Rect(28,206,220,22),"HOOK RUSH / GLYPHS",12,HudPainter.Muted,TextAnchor.MiddleLeft,true);
            lettering.Draw(new Rect(28,231,984,68),"0123456789x",44,HudPainter.White,.6f,0);
        }
    }
}
