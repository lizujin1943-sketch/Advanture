using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Read-only confirmed combo with bounded scale, bounce and a quiet break fade.</summary>
    public sealed class ComboHud
    {
        private readonly ComboLettering lettering=new ComboLettering();
        public void DrawFrame(Rect rect,ComboHudFrame frame)
        {
            if(frame.Alpha<=0 || frame.Count<1)return;
            // Sweep through green, yellow and orange to red at the capped reward tier.
            var tint=Color.HSVToRGB(Mathf.Lerp(.35f,0,frame.ColorProgress),.72f,1);
            if(frame.Alpha<1)tint=Color.Lerp(HudPainter.Muted,tint,frame.Alpha);
            tint.a=frame.Alpha;
            var number=new Rect(rect.x+8,rect.y+16+frame.OffsetY,rect.width-16,84);
            string text="x"+frame.Count;
            lettering.Draw(number,text,Mathf.RoundToInt(frame.FontSize*frame.Scale*1.12f),tint,frame.Strength,frame.Flash);
        }
    }
}
