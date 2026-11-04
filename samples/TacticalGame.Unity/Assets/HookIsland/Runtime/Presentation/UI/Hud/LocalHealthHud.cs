using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>
    /// 本地玩家的固定生命面板：渔夫头像、当前／最大生命、十段血条和身份／生存状态。
    /// 只消费调用方传入的确认值与受伤动画，不持有模拟、不发命令，也不依赖世界镜头。
    /// </summary>
    public sealed class LocalHealthHud
    {
        private const int Segments = 10;
        private readonly HudPainter ui;
        private readonly Texture2D[] portraits = new Texture2D[4];
        private static readonly Color Backdrop = HudPainter.DisplayColor(new Color(.035f,.075f,.09f,.88f));
        private static readonly Color Empty = HudPainter.DisplayColor(new Color(.12f,.17f,.19f,.94f));
        private static readonly Color Paper = HudPainter.DisplayColor(new Color(.95f,.96f,.94f));
        private static readonly Color NameInk = HudPainter.DisplayColor(new Color(.075f,.13f,.15f));
        private static readonly Color Damage = HudPainter.DisplayColor(new Color(.92f,.24f,.20f));

        public LocalHealthHud(HudPainter painter)
        {
            ui = painter;
            // 头像从本项目渔夫模型预先渲染；游戏运行时只画贴图，不额外创建相机或复制人物。
            for (int slot=0;slot<portraits.Length;slot++)
                portraits[slot] = Resources.Load<Texture2D>("UI/Portraits/FisherP"+(slot+1));
        }

        /// <summary>rect 为经过 HudLayout 缩放前的逻辑像素；now 只用于低生命的轻微边框提示。</summary>
        public void Draw(Rect rect, Fisher player, int maximum, HealthBarFeedback.Frame frame, float now)
        {
            int slot = Mathf.Clamp(player.Slot,0,portraits.Length-1);
            int current = Mathf.Max(0,player.Vitals.Health);
            float fraction = HudPainter.Fraction(current,maximum);
            bool critical = maximum>0 && fraction<=.3f;
            Color health = critical ? Damage : Paper;
            Color accent = player.Revival.Dead || player.Vitals.Downed ? Damage : FishingPresentation.PlayerColors[slot];

            var portrait = new Rect(rect.x,rect.y+4,76,92);
            float pulse = critical && !player.Revival.Dead ? .65f+.2f*Mathf.Sin(now*5) : .7f;
            ui.Surface(new Rect(portrait.x-2,portrait.y-2,portrait.width+4,portrait.height+4),new Color(accent.r,accent.g,accent.b,pulse));
            ui.Surface(portrait,Backdrop);
            ui.Surface(new Rect(portrait.x+4,portrait.y+4,portrait.width-8,portrait.height-8),new Color(accent.r*.18f,accent.g*.18f,accent.b*.18f,.9f));
            if (portraits[slot]!=null)
                GUI.DrawTexture(new Rect(portrait.x+3,portrait.y+2,70,78),portraits[slot],ScaleMode.ScaleToFit,true);
            else
                ui.Icon(new Rect(portrait.x+20,portrait.y+17,36,36),HudIcon.Heart,health);
            ui.Text(new Rect(portrait.x,portrait.yMax-20,portrait.width,15),"P"+(slot+1),10,Paper,TextAnchor.MiddleCenter,true);

            float x = rect.x+88, width = rect.width-88;
            // 当前值用大字，最大值用小字；三位、四位 HP 都不会挤在同一个字符格内。
            string value = current.ToString();
            // 缩放后的字形会取整；留足余量，避免窄窗口下最后一位数字被自动换到下一行。
            float digitsWidth = Mathf.Clamp(value.Length*16+8,36,120);
            var number = new Rect(x,rect.y+2,digitsWidth,34);
            ui.Text(new Rect(number.x+1,number.y+2,number.width,number.height),value,28,Backdrop,TextAnchor.MiddleLeft,true);
            ui.Text(number,value,28,health,TextAnchor.MiddleLeft,true);
            ui.Text(new Rect(x+digitsWidth+4,rect.y+10,width-digitsWidth-4,24),"/ "+Mathf.Max(0,maximum),17,Paper);

            var bar = new Rect(x,rect.y+40,width,20);
            DrawSegments(bar,frame,health);
            var nameplate = new Rect(x,rect.y+67,width,23);
            HudPainter.Fill(nameplate,Paper);
            HudPainter.Fill(new Rect(nameplate.x,nameplate.y,3,nameplate.height),accent);
            bool warning = player.Revival.Dead || player.Vitals.Downed || player.Unavailable;
            ui.Text(new Rect(nameplate.x+10,nameplate.y,nameplate.width-35,nameplate.height),
                player.Revival.Dead ? "已死亡" : player.Vitals.Downed ? "倒地 · 等待救援" :
                player.Unavailable ? "连接中断" : "渔夫 · P"+(slot+1),12,NameInk,TextAnchor.MiddleLeft,true);
            if (warning)
                ui.Icon(new Rect(nameplate.xMax-23,nameplate.y+4,15,15),
                    player.Revival.Dead ? HudIcon.Skull : player.Vitals.Downed ? HudIcon.Rescue : HudIcon.Alert,Damage);
            else if (critical)
                ui.Icon(new Rect(nameplate.xMax-23,nameplate.y+4,15,15),HudIcon.Heart,Damage);
            ui.Hint(rect,$"生命 · {current} / {maximum}");
        }

        private void DrawSegments(Rect rect, HealthBarFeedback.Frame frame, Color health)
        {
            // 每格代表上限的 10%，不是固定十点生命。末格按实际比例连续填充，换装后仍然准确。
            // 白色（低生命时为红色）立即到达确认值；刚失去的部分用红色短暂拖尾，复用原受伤时序。
            float gap=2, width=(rect.width-gap*(Segments-1))/Segments;
            for (int index=0;index<Segments;index++)
            {
                var cell=new Rect(rect.x+index*(width+gap),rect.y,width,rect.height);
                HudPainter.Fill(new Rect(cell.x-1,cell.y-1,cell.width+2,cell.height+2),Backdrop);
                HudPainter.Fill(cell,Empty);
                float actual=Mathf.Clamp01(frame.WhiteFrom*Segments-index);
                float trailing=Mathf.Clamp01(frame.Fill*Segments-index);
                if (trailing>actual)
                    HudPainter.Fill(new Rect(cell.x+cell.width*actual,cell.y,cell.width*(trailing-actual),cell.height),Damage);
                if (actual>0) HudPainter.Fill(new Rect(cell.x,cell.y,cell.width*actual,cell.height),health);
            }
        }
    }
}
