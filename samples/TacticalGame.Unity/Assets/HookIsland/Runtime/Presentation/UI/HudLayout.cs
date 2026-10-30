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
        public Rect Wallet => new Rect(Width-196,24,128,48);
        public Rect Menu => new Rect(Width-60,24,36,48);
        public Rect Combo => new Rect(Width/2+196,Height-142,230,118);
        public Rect Weapons => new Rect(Width/2-59,Height-76,118,52);
        public Rect Weapon(int index)=>new Rect(Weapons.x+index*64,Weapons.y,54,52);
        public Rect Tools => new Rect(Width-172,Height-70,148,46);
        public Rect Tool(int index) => new Rect(Tools.x+index*52,Tools.y,44,46);
        public Rect Construction => new Rect(24,Height-190,260,166);
        public Rect Help => new Rect(Width/2-210,Height/2-206,420,412);
        public Rect Commissions => new Rect(24,88,300,108);
        public Rect Acquisitions => new Rect(Width-144,Height-350,120,188);
        public Rect Finale=>CommissionPanel(true);
        public Rect Victory=>new Rect(Width/2-260,Height/2-192,520,384);
        public Rect CommissionPanel(bool ocean,float reveal=1)=>new Rect(-Commissions.width+(Commissions.width+24)*Mathf.Clamp01(reveal),Commissions.y,Commissions.width,Commissions.height+(ocean?28:0));
        public Rect ObjectiveTab(float reveal=1)=>new Rect(CommissionPanel(false,reveal).xMax,Commissions.y+8,24,36);
        public bool Contains(Vector2 pointer, int players, bool building,bool commissions=false,bool ocean=false,bool finale=false,float objectiveReveal=1)
        {
            return Squad(players).Contains(pointer) || Wallet.Contains(pointer) || Menu.Contains(pointer) ||
                Tools.Contains(pointer) || (building && Construction.Contains(pointer)) ||
                ((commissions || finale) && (CommissionPanel(ocean || finale,objectiveReveal).Contains(pointer) || ObjectiveTab(objectiveReveal).Contains(pointer)));
        }
    }
}
