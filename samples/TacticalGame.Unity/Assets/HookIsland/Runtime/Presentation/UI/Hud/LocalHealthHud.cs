using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>
    /// 左下角无头像生命 HUD：当前／最大生命、十段斜切血条和身份／生存状态，不绘制区域底板。
    /// 只消费调用方传入的确认值与受伤动画，不持有模拟、不发命令，也不依赖世界镜头。
    /// </summary>
    public sealed class LocalHealthHud
    {
        private const int Segments = 10;
        private readonly HudPainter ui;
        private static readonly Color Empty = HudPainter.DisplayColor(new Color(.12f,.17f,.19f,.32f));
        private static readonly Color Paper = HudPainter.DisplayColor(new Color(.95f,.96f,.94f));
        private static readonly Color Blue = HudPainter.DisplayColor(new Color(.13f,.68f,.94f));
        private static readonly Color Damage = HudPainter.DisplayColor(new Color(.92f,.24f,.20f));

        public LocalHealthHud(HudPainter painter)
        {ui = painter;}

        /// <summary>rect 为经过 HudLayout 缩放前的逻辑像素；now 只用于低生命的轻微呼吸提示。</summary>
        public void Draw(Rect rect, Fisher player, int maximum, HealthBarFeedback.Frame frame, float now)
        {
            int slot = Mathf.Clamp(player.Slot,0,3);
            int current = Mathf.Max(0,player.Vitals.Health);
            float fraction = HudPainter.Fraction(current,maximum);
            bool critical = maximum>0 && fraction<=.3f;
            Color health = critical ? Damage : Paper;
            if(critical && !player.Vitals.Downed && !player.Revival.Dead)health.a=.88f+.12f*Mathf.Sin(now*5);
            float x = rect.x+4, width = rect.width-4;
            // 当前值用大字，最大值用小字；三位、四位 HP 都不会挤在同一个字符格内。
            string value = current.ToString();
            // 缩放后的字形会取整；留足余量，避免窄窗口下最后一位数字被自动换到下一行。
            float digitsWidth = ui.TextWidth(value,40)+8;
            ui.FloatingText(new Rect(x,rect.y,digitsWidth,48),value,40,health);
            ui.FloatingText(new Rect(x+digitsWidth,rect.y+4,22,44),"/",36,Blue);
            ui.FloatingText(new Rect(x+digitsWidth+26,rect.y+10,width-digitsWidth-26,36),Mathf.Max(0,maximum).ToString(),26,Paper,bold:false);

            var bar = new Rect(rect.x,rect.y+50,rect.width,22);
            DrawSegments(bar,frame,health);
            var nameplate = new Rect(x,rect.y+76,width,24);
            bool warning = player.Revival.Dead || player.Vitals.Downed || player.Unavailable;
            ui.FloatingText(new Rect(nameplate.x,nameplate.y,nameplate.width-26,nameplate.height),
                player.Revival.Dead ? "已死亡" : player.Vitals.Downed ? "倒地 · 等待救援" :
                player.Unavailable ? "连接中断" : "渔夫 · P"+(slot+1),16,warning?Damage:Paper);
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
            // 斜切带来的空隙由遮罩本身提供；相邻贴图略有重叠，避免额外扩大每格的间距。
            float pitch=rect.width/(Segments+.28f), width=pitch*1.28f;
            for (int index=0;index<Segments;index++)
            {
                var cell=new Rect(rect.x+index*pitch,rect.y,width,rect.height);
                ui.Slanted(cell,Empty);
                float actual=Mathf.Clamp01(frame.WhiteFrom*Segments-index);
                float trailing=Mathf.Clamp01(frame.Fill*Segments-index);
                if (trailing>actual)
                    ui.Slanted(cell,Damage,actual,trailing);
                if (actual>0) ui.Slanted(cell,health,0,actual);
            }
        }
    }
}
