using NUnit.Framework;

namespace Advanture.HookIsland.Tests
{
    public sealed class HealthBarFeedbackTests
    {
        [Test] public void HitMarksLossWhiteBeforeTheRedFillDrainsAndRepeatedGuiDoesNotRestartIt()
        {
            var feedback = new HealthBarFeedback(); var session = new object();
            feedback.BeginFrame(session, 1, 1, 0, true); feedback.Observe(0, 100, 100);
            feedback.BeginFrame(session, 1, 2, .01f, true); var hit = feedback.Observe(0, 60, 100);
            Assert.That(hit.Fill, Is.EqualTo(1)); Assert.That(hit.WhiteFrom, Is.EqualTo(.6f)); Assert.That(hit.WhiteTo, Is.EqualTo(1));
            feedback.BeginFrame(session, 1, 2, .07f, true); Assert.That(feedback.Observe(0, 60, 100).Fill, Is.EqualTo(1));
            feedback.BeginFrame(session, 1, 2, .27f, true); var drain = feedback.Observe(0, 60, 100);
            Assert.That(drain.Fill, Is.InRange(.7f, .9f)); Assert.That(drain.WhiteTo, Is.EqualTo(drain.WhiteFrom));
            feedback.BeginFrame(session, 1, 2, .44f, true); Assert.That(feedback.Observe(0, 60, 100).Fill, Is.EqualTo(.6f));
        }
        [Test] public void EquipmentChangesHiddenUiAndNewRoundsNeverReplayLoss()
        {
            var feedback = new HealthBarFeedback(); var session = new object();
            feedback.BeginFrame(session, 1, 1, 0, true); feedback.Observe(0, 180, 200);
            feedback.BeginFrame(session, 1, 2, .1f, true); var equipment = feedback.Observe(0, 100, 100);
            Assert.That(equipment.Fill, Is.EqualTo(1)); Assert.That(equipment.WhiteTo, Is.EqualTo(equipment.WhiteFrom));
            feedback.BeginFrame(session, 1, 3, .2f, true); feedback.Observe(0, 40, 100);
            feedback.BeginFrame(session, 1, 4, .25f, true); var heal=feedback.Observe(0,80,100);
            Assert.That(heal.Fill,Is.EqualTo(.4f));Assert.That(heal.HealingTo,Is.EqualTo(.8f));
            feedback.BeginFrame(session, 1, 5, .3f, false); feedback.Observe(0, 30, 100);
            feedback.BeginFrame(session, 1, 6, .35f, true); Assert.That(feedback.Observe(0, 30, 100).Fill, Is.EqualTo(.3f));
            feedback.BeginFrame(session, 2, 0, .4f, true); var restart = feedback.Observe(0, 10, 100);
            Assert.That(restart.Fill, Is.EqualTo(.1f)); Assert.That(restart.WhiteTo, Is.EqualTo(restart.WhiteFrom));
        }
        [Test] public void HealingShowsGreenImmediatelyThenWhiteCatchesUpWithoutRestartingOnTheSameTick()
        {
            var feedback=new HealthBarFeedback();var session=new object();
            feedback.BeginFrame(session,1,1,0,true);feedback.Observe(0,40,100);
            feedback.BeginFrame(session,1,2,.01f,true);var start=feedback.Observe(0,80,100);
            Assert.That(start.HealingFrom,Is.EqualTo(.4f));Assert.That(start.HealingTo,Is.EqualTo(.8f));
            Assert.That(start.WhiteTo,Is.EqualTo(start.WhiteFrom),"治疗不能产生白色受伤闪烁。");
            feedback.BeginFrame(session,1,2,.1f,true);Assert.That(feedback.Observe(0,80,100).HealingFrom,Is.EqualTo(.4f));
            feedback.BeginFrame(session,1,2,.32f,true);var middle=feedback.Observe(0,80,100);
            Assert.That(middle.HealingFrom,Is.EqualTo(.6f).Within(.001f));Assert.That(middle.HealingTo,Is.EqualTo(.8f));
            feedback.BeginFrame(session,1,2,.49f,true);var end=feedback.Observe(0,80,100);
            Assert.That(end.Fill,Is.EqualTo(.8f));Assert.That(end.HealingFrom,Is.EqualTo(end.HealingTo));
        }
        [Test] public void RepeatedHealingContinuesAndDamageCancelsGreenWithoutLeakingAcrossPlayers()
        {
            var feedback=new HealthBarFeedback();var session=new object();
            feedback.BeginFrame(session,1,1,0,true);feedback.Observe(0,30,100);feedback.Observe(1,70,100);
            feedback.BeginFrame(session,1,2,.01f,true);feedback.Observe(0,60,100);
            feedback.BeginFrame(session,1,3,.25f,true);var first=feedback.Observe(0,90,100);
            feedback.BeginFrame(session,1,3,.28f,true);Assert.That(feedback.Observe(0,90,100).HealingFrom,Is.GreaterThan(first.HealingFrom));
            feedback.BeginFrame(session,1,4,.3f,true);var damage=feedback.Observe(0,50,100);
            Assert.That(damage.HealingFrom,Is.EqualTo(damage.HealingTo),"新伤害应取消未完成的治疗绿条。");
            Assert.That(damage.WhiteFrom,Is.EqualTo(.5f));Assert.That(damage.Fill,Is.EqualTo(.9f));
            var other=feedback.Observe(1,70,100);Assert.That(other.Fill,Is.EqualTo(.7f));
            Assert.That(other.HealingFrom,Is.EqualTo(other.HealingTo));
            feedback.BeginFrame(session,2,0,.4f,true);var restored=feedback.Observe(0,100,100);
            Assert.That(restored.Fill,Is.EqualTo(1));Assert.That(restored.HealingFrom,Is.EqualTo(restored.HealingTo));
        }
        [Test] public void RepeatedHitsRetargetAnExistingDrainAndOtherPlayersRemainIndependent()
        {
            var feedback = new HealthBarFeedback(); var session = new object();
            feedback.BeginFrame(session, 1, 1, 0, true); feedback.Observe(0, 100, 100); feedback.Observe(1, 70, 100);
            feedback.BeginFrame(session, 1, 2, .01f, true); feedback.Observe(0, 80, 100);
            feedback.BeginFrame(session, 1, 3, .15f, true); var repeated = feedback.Observe(0, 40, 100);
            Assert.That(repeated.Fill, Is.LessThan(1));
            feedback.BeginFrame(session, 1, 3, .18f, true); Assert.That(feedback.Observe(0, 40, 100).Fill, Is.LessThan(repeated.Fill));
            var other = feedback.Observe(1, 70, 100); Assert.That(other.Fill, Is.EqualTo(.7f)); Assert.That(other.WhiteTo, Is.EqualTo(other.WhiteFrom));
            feedback.BeginFrame(session, 1, 3, .49f, true); Assert.That(feedback.Observe(0, 40, 100).Fill, Is.EqualTo(.4f));
            feedback.Observe(0, 0, 100, true);
            feedback.BeginFrame(session, 1, 4, .5f, true); Assert.That(feedback.Observe(0, 20, 100).Fill, Is.EqualTo(.2f));
        }
    }
}
