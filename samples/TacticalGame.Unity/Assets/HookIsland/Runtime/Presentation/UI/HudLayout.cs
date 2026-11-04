using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Logical HUD geometry shared by rendering and gameplay pointer blocking.</summary>
    public readonly struct HudLayout
    {
        public readonly float Width, Height;
        public HudLayout(float width, float height) { Width=width; Height=height; }
        public static float Scale(float width, float height) => Mathf.Clamp(Mathf.Min(width/1280f,height/720f),.45f,2f);
        /// <summary>只为实际队友保留空间；单人游戏的队伍区域宽度为零。</summary>
        public Rect Squad(int teammates,float reveal=0,bool ocean=false) => new Rect(32,Mathf.Lerp(112,CommissionPanel(ocean).yMax+12,Mathf.Clamp01(reveal)),Mathf.Clamp(teammates,0,3)*80,52);
        public Rect Wallet => new Rect(Width-196,26,164,48);
        public Rect Combo => new Rect(Width/2+240,Height-220,210,118);
        public Rect FirearmAmmo => new Rect(Width-182,Height-118,150,86);
        /// <summary>医疗罐占据左下角，包含左下角次数；与生命条共用 32 像素底部安全边距。</summary>
        public Rect LocalHealing => new Rect(32,Height-136,78,104);
        /// <summary>生命条位于医疗罐右侧，预留 6 像素间距；坐标与其他 HUD 一起缩放。</summary>
        public Rect LocalHealth => new Rect(LocalHealing.xMax+6,Height-136,260,104);
        // 扩建菜单展开在生命面板上方，建造时仍能看到自己的实时血量。
        public Rect Construction => new Rect(32,LocalHealing.y-180,260,166);
        public Rect Help => new Rect(Width/2-210,Height/2-206,420,412);
        public Rect ObjectiveSummary => new Rect(32,26,340,66);
        public Rect Commissions => new Rect(32,104,300,108);
        public Rect Acquisitions => new Rect(Width-144,Height-350,120,188);
        public Rect Finale=>CommissionPanel(true);
        public Rect Victory=>new Rect(Width/2-260,Height/2-192,520,384);
        public Rect CommissionPanel(bool ocean,float reveal=1)=>new Rect(-Commissions.width+(Commissions.width+Commissions.x)*Mathf.Clamp01(reveal),Commissions.y,Commissions.width,Commissions.height+(ocean?28:0));
        public Rect ObjectiveTab(float reveal=0)=>new Rect(ObjectiveSummary.x+242,ObjectiveSummary.y+4,98,36);
        public bool Contains(Vector2 pointer, int teammates, bool building,bool commissions=false,bool ocean=false,bool finale=false,float objectiveReveal=0,bool showObjectives=true)
        {
            bool objectives=showObjectives && (commissions || finale);
            // 只阻挡真正可见的区域。旧物品栏、菜单图标和工具按钮移除后不能留下透明的点击拦截区。
            return (teammates>0 && Squad(teammates,objectives?objectiveReveal:0,ocean || finale).Contains(pointer)) ||
                Wallet.Contains(pointer) || LocalHealing.Contains(pointer) || LocalHealth.Contains(pointer) || (building && Construction.Contains(pointer)) ||
                (objectives && (ObjectiveTab().Contains(pointer) || (objectiveReveal>0 && CommissionPanel(ocean || finale,objectiveReveal).Contains(pointer))));
        }
    }
}
