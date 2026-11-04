using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>
    /// 血条左侧的个人医疗罐：白色外轮廓、按剩余次数填充的储液腔，以及左下角数字。
    /// 只读取调用方传入的确认态，不从生命比例推算次数，也不处理使用或补充请求。
    /// </summary>
    public sealed class HealingChargesHud
    {
        private readonly HudPainter ui;
        public HealingChargesHud(HudPainter painter){ui=painter;}
        public void Draw(Rect rect,Fisher player,HealingConfig config)
        {
            int remaining=Mathf.Clamp(player.HealingCharges,0,config.Charges);
            // 初始 3/3 全满；只有确认成功消耗后才变为 2/3、1/3、0。
            // 满血使用失败、预测回滚和暂停操作不会自行扣减或隐藏已有次数。
            ui.HealingCanister(rect,HudPainter.Fraction(remaining,config.Charges));
            // 次数压住瓶身左下边缘，沿用设计图的大号斜体和窄深色描边，避免满罐时白字融入白色液体。
            float scale=rect.height/120;
            ui.SlantedNumber(new Rect(rect.x,rect.yMax-48*scale,50*scale,48*scale),remaining.ToString(),
                Mathf.RoundToInt((remaining<10?44:32)*scale),Color.white,outlined:true);
            ui.Hint(rect,$"医疗储罐 · 剩余 {remaining}/{config.Charges} 次");
        }
    }
}
