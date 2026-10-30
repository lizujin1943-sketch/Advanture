using NUnit.Framework;

namespace Advanture.HookIsland.Tests
{
    public sealed class StaminaDrainFeedbackTests
    {
        [Test] public void LossLeavesABriefTrailThenSettlesAndRecoveryNeverLooksLikeLoss()
        {
            var feedback = new StaminaDrainFeedback();
            Assert.That(feedback.Sample(1, 0), Is.EqualTo(1));
            Assert.That(feedback.Sample(.5f, .01f), Is.EqualTo(1));
            Assert.That(feedback.Sample(.5f, .15f), Is.EqualTo(1));
            Assert.That(feedback.Sample(.5f, .3f), Is.InRange(.51f, .99f));
            feedback.Sample(.5f, .45f); feedback.Sample(.5f, .6f);
            Assert.That(feedback.Sample(.5f, .75f), Is.EqualTo(.5f));
            Assert.That(feedback.Sample(.7f, .8f), Is.EqualTo(.7f));
        }
        [Test] public void RepeatedGuiDoesNotConsumeTheTrailAndLongHiddenPeriodsRebaseline()
        {
            var feedback = new StaminaDrainFeedback(); feedback.Sample(1, 0);
            float trail = feedback.Sample(.2f, .1f);
            Assert.That(feedback.Sample(.2f, .1f), Is.EqualTo(trail));
            Assert.That(feedback.Sample(.1f, 4), Is.EqualTo(.1f));
        }
    }
}
