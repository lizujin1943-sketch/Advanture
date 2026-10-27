using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Logical HUD geometry shared by rendering and gameplay pointer blocking.</summary>
    public readonly struct HudLayout
    {
        public readonly float Width, Height;
        public HudLayout(float width, float height) { Width=width; Height=height; }
        public static float Scale(float width, float height) => Mathf.Clamp(Mathf.Min(width/1280f,height/720f),.45f,2f);
        public Rect Squad(int players) => new Rect(24,24,16+Mathf.Clamp(players,1,4)*52,52);
        public Rect Resources(bool materials) => new Rect(Width-(materials?348:164),24,materials?280:96,48);
        public Rect Menu => new Rect(Width-60,24,36,48);
        public Rect Vitals => new Rect(Width/2-180,Height-104,360,80);
        public Rect Tools => new Rect(Width-172,Height-70,148,46);
        public Rect Tool(int index) => new Rect(Tools.x+index*52,Tools.y,44,46);
        public Rect Construction => new Rect(24,Height-190,260,166);
        public Rect Help => new Rect(Width/2-210,Height/2-206,420,412);
        public Rect Commissions => new Rect(24,88,300,108);
        public Rect Merchants=>new Rect(Width-244,88,220,176);
        public Rect Finale=>CommissionPanel(true);
        public Rect Victory=>new Rect(Width/2-260,Height/2-192,520,384);
        public Rect CommissionPanel(bool ocean)=>new Rect(Commissions.x,Commissions.y,Commissions.width,Commissions.height+(ocean?28:0));
        public bool Contains(Vector2 pointer, int players, bool materials, bool vitals, bool building,bool commissions=false,bool ocean=false,bool finale=false,bool merchants=false)
        {
            return Squad(players).Contains(pointer) || Resources(materials).Contains(pointer) || Menu.Contains(pointer) ||
                (vitals && Vitals.Contains(pointer)) || Tools.Contains(pointer) || (building && Construction.Contains(pointer)) ||
                ((commissions || finale) && CommissionPanel(ocean || finale).Contains(pointer)) || (merchants && Merchants.Contains(pointer));
        }
    }
}
