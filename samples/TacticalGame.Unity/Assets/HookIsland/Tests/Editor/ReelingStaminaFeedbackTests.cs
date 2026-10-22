using Advanture.HookIsland.Simulation;
using NUnit.Framework;

namespace Advanture.HookIsland.Tests
{
    public sealed class ReelingStaminaFeedbackTests
    {
        private static Fisher Player(ushort slot = 0) => new Fisher { Slot = slot, FishId = -1, HookedPlayer = -1, DriftwoodId = -1 };

        [Test] public void ShowsOnlyReturningFishAndPiranhas()
        {
            var feedback = new ReelingStaminaFeedback();
            var player = Player();
            Assert.That(feedback.Observe(player, 1, 0), Is.EqualTo(ReelingFeedbackMode.Hidden));
            player.Phase = HookPhase.Flying; player.FishId = 0;
            Assert.That(feedback.Observe(player, 2, .1), Is.EqualTo(ReelingFeedbackMode.Hidden));
            player.Phase = HookPhase.Returning;
            Assert.That(feedback.Observe(player, 3, .2), Is.EqualTo(ReelingFeedbackMode.Reeling));
            player.FishId = -1;
            Assert.That(feedback.Observe(player, 4, .3), Is.EqualTo(ReelingFeedbackMode.Hidden));
            player.HookedPlayer = 1;
            Assert.That(feedback.Observe(player, 5, .4), Is.EqualTo(ReelingFeedbackMode.Hidden));
            player.HookedPlayer = -1; player.DriftwoodId = 0;
            Assert.That(feedback.Observe(player, 6, .5), Is.EqualTo(ReelingFeedbackMode.Hidden));
            player.HookedPlayer = -1; player.DriftwoodId = -1; player.HookedPiranha = 1;
            Assert.That(feedback.Observe(player, 7, .6), Is.EqualTo(ReelingFeedbackMode.Reeling));
        }

        [Test] public void ConfirmedEscapeShowsOnceAndRepeatedGuiFramesDoNotExtendIt()
        {
            var feedback = new ReelingStaminaFeedback(); var player = Player();
            player.Phase = HookPhase.Returning; player.FishId = 0;
            feedback.Observe(player, 9, 0);
            player.FishId = -1; player.Escapes = 1; player.Stamina = 0;
            Assert.That(feedback.Observe(player, 10, 1), Is.EqualTo(ReelingFeedbackMode.Escaped));
            Assert.That(feedback.Observe(player, 10, 2.7), Is.EqualTo(ReelingFeedbackMode.Escaped));
            Assert.That(feedback.Observe(player, 11, 3), Is.EqualTo(ReelingFeedbackMode.Hidden));
            player.Escapes++;
            Assert.That(feedback.Observe(player, 12, 3.1), Is.EqualTo(ReelingFeedbackMode.Escaped));
        }

        [Test] public void CancellationAndSuccessfulLandingDoNotAnnounceAnEscape()
        {
            var feedback = new ReelingStaminaFeedback(); var player = Player();
            player.Phase = HookPhase.Returning; player.FishId = 0;
            feedback.Observe(player, 1, 0);
            player.FishId = -1;
            Assert.That(feedback.Observe(player, 2, .1), Is.EqualTo(ReelingFeedbackMode.Hidden));
            player.Phase = HookPhase.Ready; player.Catches = 1;
            Assert.That(feedback.Observe(player, 3, .2), Is.EqualTo(ReelingFeedbackMode.Hidden));
        }

        [Test] public void JoiningAndRestartingDoNotReplayEscapesFromAnEarlierSession()
        {
            var feedback = new ReelingStaminaFeedback(); var player = Player(); player.Escapes = 4;
            Assert.That(feedback.Observe(player, 100, 1), Is.EqualTo(ReelingFeedbackMode.Hidden));
            player.Escapes++;
            Assert.That(feedback.Observe(player, 101, 2), Is.EqualTo(ReelingFeedbackMode.Escaped));
            feedback.Reset();
            Assert.That(feedback.Observe(player, 101, 2.1), Is.EqualTo(ReelingFeedbackMode.Hidden));
            player.Escapes++;
            feedback.Observe(player, 102, 3);
            Assert.That(feedback.Observe(player, 0, 3.1), Is.EqualTo(ReelingFeedbackMode.Hidden));
            player.Escapes = 0;
            Assert.That(feedback.Observe(player, 1, 3.2), Is.EqualTo(ReelingFeedbackMode.Hidden));
        }

        [Test] public void NewCatchReplacesOldNoticeAndEachPlayersFeedbackIsIndependent()
        {
            var feedback = new ReelingStaminaFeedback(); var first = Player(); var second = Player(1);
            feedback.Observe(first, 0, 0); feedback.Observe(second, 0, 0);
            first.Escapes = 1;
            Assert.That(feedback.Observe(first, 1, .1), Is.EqualTo(ReelingFeedbackMode.Escaped));
            Assert.That(feedback.Observe(second, 1, .1), Is.EqualTo(ReelingFeedbackMode.Hidden));
            first.FishId = 0; first.Phase = HookPhase.Returning;
            Assert.That(feedback.Observe(first, 2, .2), Is.EqualTo(ReelingFeedbackMode.Reeling));
            first.FishId = -1;
            Assert.That(feedback.Observe(first, 3, .3), Is.EqualTo(ReelingFeedbackMode.Hidden));
        }
    }
}
