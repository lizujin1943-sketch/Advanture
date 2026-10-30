using Advanture.HookIsland.Simulation;
using NUnit.Framework;

namespace Advanture.HookIsland.Tests
{
    public sealed class ComboHudTests
    {
        [Test] public void ConfirmedIncreaseBouncesOnceAndTeammateOrRepeatedGuiDoesNotReplay()
        {
            var a=new ComboHudAnimation();var session=new object();var c=new ComboConfig();
            a.Observe(session,1,1,2,c,0);Assert.That(a.Sample(0).Alpha,Is.Zero);
            a.Observe(session,1,2,3,c,1);a.ConfirmIncrease(3,1);var bounce=a.Sample(1.08f);
            Assert.That(bounce.Count,Is.EqualTo(3));Assert.That(bounce.Bonus,Is.EqualTo(10));
            Assert.That(bounce.Scale,Is.GreaterThan(1));Assert.That(bounce.OffsetY,Is.LessThan(0));
            a.Observe(session,1,2,3,c,2);a.Observe(session,1,3,3,c,2);
            Assert.That(a.Sample(2).Scale,Is.EqualTo(1));Assert.That(a.Sample(2).Flash,Is.Zero);
        }
        [Test] public void HigherBonusIsLargerAndStrongerButBeyondCapOnlyCountGrows()
        {
            var a=new ComboHudAnimation();var s=new object();var c=new ComboConfig();
            a.Observe(s,1,1,2,c,0);a.Observe(s,1,2,3,c,1);a.ConfirmIncrease(3,1);var low=a.Sample(1.08f);
            a.Observe(s,1,3,10,c,2);a.ConfirmIncrease(10,2);var high=a.Sample(2.08f);
            Assert.That(high.FontSize,Is.GreaterThan(low.FontSize));Assert.That(high.Scale,Is.GreaterThan(low.Scale));
            Assert.That(low.ColorProgress,Is.Zero);Assert.That(high.ColorProgress,Is.EqualTo(1));
            a.Observe(s,1,4,100,c,3);a.ConfirmIncrease(100,3);var later=a.Sample(3.08f);
            Assert.That(later.Count,Is.EqualTo(100));Assert.That(later.Bonus,Is.EqualTo(50));
            Assert.That(later.FontSize,Is.EqualTo(high.FontSize));Assert.That(later.Scale,Is.EqualTo(high.Scale).Within(.0001));
            Assert.That(later.ColorProgress,Is.EqualTo(high.ColorProgress));
        }
        [Test] public void BreakFadesAndNewSessionRestoresWithoutReplayingHistoricalSuccess()
        {
            var a=new ComboHudAnimation();var s=new object();var c=new ComboConfig();
            a.Observe(s,1,1,9,c,0);Assert.That(a.Sample(0).Flash,Is.Zero);
            a.Observe(s,1,2,0,c,1);Assert.That(a.Sample(1.12f).Alpha,Is.InRange(.4f,.6f));
            Assert.That(a.Sample(1.3f).Alpha,Is.Zero);
            a.Observe(s,2,1,0,c,2);Assert.That(a.Sample(2).Alpha,Is.Zero);
            a.Observe(new object(),2,200,12,c,3);
            Assert.That(a.Sample(3).Count,Is.EqualTo(12));Assert.That(a.Sample(3).Flash,Is.Zero);
        }
    }
}
