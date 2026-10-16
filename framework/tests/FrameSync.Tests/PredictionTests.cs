using System;
using System.Collections.Generic;
using System.Linq;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;

namespace FrameSync.Tests
{
    internal static class PredictionTests
    {
        public static IEnumerable<(string, Action)> Cases()
        {
            // 未采样不等于无操作：如果框架仍偷偷用本地中性值，这些行为断言会失败。
            yield return ("prediction.freeze_missing_local_input_stops_before_execution", () => {
                var f = new SyncFixture(prediction: new PredictionOptions()); f.Start();
                f.A.Poll(4); f.A.Poll();
                Assert.Equal(0UL, f.A.PredictedTick); Assert.Equal(0UL, f.A.PresentedTick);
                Assert.Equal(0, f.A.PendingLocalInputCount); Assert.Equal(0, f.A.LastPredictionEvents.Count);
                Assert.Equal(0UL, f.A.RollbackCount);
            });
            yield return ("prediction.freeze_neutral_is_explicit_immutable_and_matches_final", () => {
                var f = new SyncFixture(prediction: new PredictionOptions()); f.Start();
                byte[] input = {0}; Assert.True(f.A.TrySubmit(1, input)); input[0] = 9;
                Assert.True(f.A.TrySubmit(1, new byte[] {0}));
                Assert.Throws<InvalidOperationException>(() => f.A.TrySubmit(1, new byte[] {5}));
                f.B.TrySubmit(1, new byte[] {0}); f.A.Poll(2); f.B.Poll();
                Assert.Equal(1UL, f.A.PredictedTick); Assert.Equal(0L, f.A.InspectPredictedState());
                f.Server.Poll(TimeSpan.Zero); f.Finish(1); Drain(f);
                f.AssertEqual(1, 0); Assert.Equal(0UL, f.A.RollbackCount);
                Assert.True(f.A.LastFrame!.Inputs.All(x => x.Origin == InputOrigin.Submitted));
                Assert.Equal(0, f.A.PendingLocalInputCount);
            });
            yield return ("prediction.freeze_gap_blocks_later_input_until_filled", () => {
                var f = new SyncFixture(prediction: new PredictionOptions()); f.Start();
                f.A.TrySubmit(1, new byte[] {2}); f.A.TrySubmit(3, new byte[] {7}); f.A.Poll(4);
                Assert.Equal(1UL, f.A.PredictedTick); Assert.Equal(2L, f.A.InspectPredictedState());
                f.A.TrySubmit(2, new byte[] {0}); f.A.Poll();
                Assert.Equal(3UL, f.A.PredictedTick); Assert.Equal(9L, f.A.InspectPredictedState());
                Assert.Equal(0UL, f.A.RollbackCount); // 补齐的是尚未执行的帧，不需要“本地输入晚到”回滚。
            });
            yield return ("prediction.freeze_rejected_submission_does_not_unlock_prediction", () => {
                var f = new SyncFixture(prediction: new PredictionOptions(),
                    clientOptions: new SessionOptions(maxOutboxMessages: 2)); f.Start();
                Assert.True(f.A.TrySubmit(2, new byte[] {7})); Assert.True(f.A.TrySubmit(3, new byte[] {0}));
                Assert.True(!f.A.TrySubmit(1, new byte[] {2})); f.A.Poll(2);
                Assert.Equal(0UL, f.A.PredictedTick);
                Assert.True(f.A.TrySubmit(1, new byte[] {2})); f.A.Poll();
                Assert.Equal(2UL, f.A.PredictedTick); Assert.Equal(9L, f.A.InspectPredictedState());
                Assert.Equal(0UL, f.A.RollbackCount);
            });
            yield return ("prediction.freeze_final_frame_does_not_need_local_submission", () => {
                var f = new SyncFixture(prediction: new PredictionOptions()); f.Start();
                f.A.Poll(4); f.Finish(1); Drain(f);
                f.AssertEqual(1, 0); Assert.Equal(1UL, f.A.PredictedTick);
                Assert.Equal(0UL, f.A.RollbackCount);
                f.A.TrySubmit(2, new byte[] {7}); f.A.Poll();
                Assert.Equal(2UL, f.A.PredictedTick); Assert.Equal(7L, f.A.InspectPredictedState());
            });
            yield return ("prediction.inactive_local_member_only_requires_input_after_activation", () => {
                var f = new SyncFixture(initiallyActive: false, prediction: new PredictionOptions()); f.Start();
                // 非活跃成员没有输入槽，允许预测空成员世界；收到启用事件后恢复并按新成员表重算。
                f.A.Poll(3); Assert.Equal(3UL, f.A.PredictedTick); f.Finish(1); Drain(f);
                Assert.Equal(1UL, f.A.ConfirmedTick); Assert.Equal(1UL, f.A.PredictedTick);
                Assert.True(f.A.IsReplaying); Assert.True(f.A.LocalParticipant!.IsActive);
                FreezeLocal(f, 2, 3); Drain(f);
                Assert.Equal(3UL, f.A.PredictedTick); Assert.True(!f.A.IsReplaying);
                Assert.Equal(1UL, f.A.RollbackCount);
            });
            yield return ("prediction.local_response_precedes_confirmation", () => {
                var f = new SyncFixture(prediction: new PredictionOptions()); f.Start();
                f.A.TrySubmit(1, new byte[] {5}); f.B.TrySubmit(1, new byte[] {0}); f.B.Poll(); f.A.Poll(1);
                Assert.Equal(5L, f.A.InspectPredictedState()); Assert.Equal(0L, f.A.InspectState());
                Assert.Equal(0UL, f.A.CaptureCheckpoint().Tick); Assert.Equal(1UL, f.A.PredictedTick);
                Assert.True(f.A.LastPredictionEvents.Any(x => x.Kind == PredictionEventKind.Predicted));
                f.Server.Poll(TimeSpan.Zero); f.Finish(1); Drain(f);
                f.AssertEqual(1, 5); Assert.Equal(0UL, f.A.RollbackCount);
            });
            yield return ("prediction.remote_input_restores_preceding_frame_and_replays", () => {
                var f = new SyncFixture(prediction: new PredictionOptions()); f.Start();
                FreezeLocal(f, 1, 4); f.B.TrySubmit(1, new byte[] {0}); f.A.Poll(4); Assert.Equal(4UL, f.A.PredictedTick);
                f.B.TrySubmit(2, new byte[] {7}); f.B.Poll(); f.Server.Poll(TimeSpan.Zero);
                f.Finish(2); Drain(f);
                f.AssertEqual(2, 7); Assert.Equal(4UL, f.A.PredictedTick);
                Assert.Equal(7L, f.A.InspectPredictedState()); Assert.Equal(1UL, f.A.RollbackCount);
                Assert.Equal(1UL, f.A.LastRestoreTick);
            });
            yield return ("prediction.late_local_input_is_removed_without_resubmitting", () => {
                var f = new SyncFixture(prediction: new PredictionOptions()); f.Start();
                f.A.TrySubmit(1, new byte[] {5}); FreezeLocal(f, 2, 3); f.A.Poll(3); f.Finish(1); Drain(f);
                Assert.Equal(0L, f.A.InspectState()); Assert.Equal(0L, f.A.InspectPredictedState());
                Assert.True(f.A.RollbackCount > 0); Assert.Equal(1UL, f.Server.RejectedMessages);
                f.Finish(3); Drain(f); f.AssertEqual(3, 0);
            });
            yield return ("prediction.identical_bytes_with_different_origin_still_corrects_checkpoint", () => {
                var f = new SyncFixture(prediction: new PredictionOptions()); f.Start();
                FreezeLocal(f, 1, 1); f.A.Poll(1); f.Server.Poll(TimeSpan.Zero);
                // B 未提交，服务器补位的字节虽同为 0，来源和缺失计数仍与预测不同。
                f.Finish(1); Drain(f); f.AssertEqual(1, 0); Assert.Equal(1UL, f.A.RollbackCount);
            });
            yield return ("prediction.replay_budget_keeps_published_view_stable", () => {
                var f = new SyncFixture(prediction: new PredictionOptions(), clientOptions: new SessionOptions(maxFramesPerPoll: 1)); f.Start();
                FreezeLocal(f, 1, 3); f.A.TrySubmit(4, new byte[] {5}); for(int i=0;i<5;i++) f.A.Poll(4);
                Assert.Equal(5L, f.A.InspectPredictedState());
                f.B.TrySubmit(1, new byte[] {7}); f.B.Poll(); f.Server.Poll(TimeSpan.Zero); f.Finish(1);
                Assert.True(f.A.IsReplaying); Assert.Equal(5L, f.A.InspectPredictedState());
                Assert.True(f.A.TrySubmit(5, new byte[] {2})); // 重算分批时仍可收集和发送新输入。
                Drain(f); Assert.True(!f.A.IsReplaying); Assert.Equal(12L, f.A.InspectPredictedState());
            });
            yield return ("prediction.window_is_bounded_and_close_discards_unconfirmed_tail", () => {
                var f = new SyncFixture(prediction: new PredictionOptions(maxPredictionFrames: 3)); f.Start();
                FreezeLocal(f, 1, 2); f.A.TrySubmit(3, new byte[] {5}); FreezeLocal(f, 4, 4); f.A.Poll(1000); Assert.Equal(3UL, f.A.PredictedTick);
                f.Server.Close(); f.Server.Poll(TimeSpan.Zero); f.A.Poll();
                Assert.Equal(SessionStatus.Closed, f.A.Status); Assert.Equal(0UL, f.A.PredictedTick);
                Assert.Equal(0L, f.A.InspectPredictedState());
                Assert.True(f.A.LastPredictionEvents.Any(x => x.Kind == PredictionEventKind.Invalidated));
            });
            yield return ("prediction.confirmed_events_deliver_once_and_bad_hash_never_confirms", () => {
                var f = new SyncFixture(prediction: new PredictionOptions()); f.Start(); FreezeLocal(f, 1, 2); f.A.Poll(2);
                f.Server.Poll(TimeSpan.FromMilliseconds(100));
                var results = f.A.Poll(); Assert.Equal(1, results.Count);
                Assert.Equal(1, f.A.LastPredictionEvents.Count(x => x.Kind == PredictionEventKind.Confirmed));
                Assert.Equal(0, f.A.Poll().Count);
                var wrong = Fixture.Frame(2, Fixture.Input(0, 0), Fixture.Input(1, 0));
                SessionBoundaryTests.Send(f.ServerTransport, 2, MessageKind.Final, Wire.Final(wrong, new string('0',64),900));
                Assert.Equal(0, f.A.Poll().Count); Assert.Equal(SessionStatus.Failed, f.A.Status);
                Assert.Equal(1UL, f.A.ConfirmedTick);
                Assert.Equal(2UL,f.A.Desync!.Frame.Tick);
                Assert.Equal(1UL,f.A.Desync.LastConfirmed.Tick);
                Assert.True(f.A.Desync.ExpectedHash!=f.A.Desync.ActualHash);
            });
            yield return ("prediction.member_deactivation_is_replayed", () => {
                var f = new SyncFixture(prediction: new PredictionOptions()); f.Start(); FreezeLocal(f, 1, 4); f.A.Poll(4);
                f.Server.Deactivate(3); f.Finish(1); Drain(f);
                Assert.True(!f.A.CaptureCheckpoint().Membership.Find(1).IsActive);
                Assert.True(f.A.RollbackCount > 0); Assert.Equal(SessionStatus.Running, f.A.Status);
            });
            yield return ("prediction.frame_104_restores_99_when_frame_100_changes", () => {
                var f = new SyncFixture(prediction:new PredictionOptions());f.Start();
                for(ulong tick=1;tick<=99;tick++){f.Finish(tick);Drain(f);}
                FreezeLocal(f, 100, 104);f.A.Poll(104);Assert.Equal(104UL,f.A.PredictedTick);
                f.B.TrySubmit(100,new byte[]{7});f.B.Poll();f.Server.Poll(f.Now);f.Finish(100);Drain(f);
                Assert.Equal(99UL,f.A.LastRestoreTick);Assert.Equal(104UL,f.A.PredictedTick);
                Assert.Equal(7L,f.A.InspectPredictedState());f.AssertEqual(100,7);
            });
            yield return ("prediction.changed_event_payload_invalidates_then_confirms_once", () => {
                var f=new SyncFixture(prediction:new PredictionOptions(),
                    serverGame:new SyncTestGame{EventFromValue=true},clientAGame:new SyncTestGame{EventFromValue=true},clientBGame:new SyncTestGame{EventFromValue=true});
                f.Start();FreezeLocal(f, 1, 1);f.A.Poll(1);Assert.Equal((byte)0,f.A.LastPredictionEvents.Single().Event.Payload.Span[0]);
                f.B.TrySubmit(1,new byte[]{7});f.B.Poll();f.Server.Poll(TimeSpan.Zero);f.Finish(1);
                var changes=f.A.LastPredictionEvents;
                Assert.Equal(2,changes.Count);Assert.Equal(PredictionEventKind.Invalidated,changes[0].Kind);
                Assert.Equal((byte)0,changes[0].Event.Payload.Span[0]);Assert.Equal(PredictionEventKind.Confirmed,changes[1].Kind);
                Assert.Equal((byte)7,changes[1].Event.Payload.Span[0]);Assert.Equal(0,f.A.Poll().Count);
                Assert.Equal(0,f.A.LastPredictionEvents.Count);
            });
            yield return ("prediction.final_overrides_queued_local_input_without_speculative_local_execution", () => {
                var f=new SyncFixture(prediction:new PredictionOptions());f.Start();f.A.Poll(3);
                f.A.TrySubmit(1,new byte[]{5});f.A.TrySubmit(2,new byte[]{7});
                f.Server.Poll(TimeSpan.FromMilliseconds(100)); // 第1帧已补中性，客户端尚未发送自己的两份输入。
                f.A.Poll();Drain(f);
                Assert.Equal(0UL,f.A.RollbackCount); // 第1帧此前未预测；直接执行服务器的中性最终帧。
                Assert.Equal(2UL,f.A.PredictedTick); // 第3帧尚无冻结输入，停在第2帧，不再盲目跑到3。
                Assert.Equal(7L,f.A.InspectPredictedState());
            });
            yield return ("prediction.second_correction_during_partial_replay", () => {
                var f=new SyncFixture(prediction:new PredictionOptions(),clientOptions:new SessionOptions(maxFramesPerPoll:1));
                f.Start();FreezeLocal(f, 1, 4);for(int i=0;i<4;i++)f.A.Poll(4);
                f.B.TrySubmit(1,new byte[]{3});f.B.TrySubmit(2,new byte[]{7});f.B.Poll();f.Server.Poll(TimeSpan.Zero);
                f.Server.Poll(TimeSpan.FromMilliseconds(100));f.A.Poll();f.A.Poll();f.A.Poll();
                Assert.True(f.A.IsReplaying);Assert.Equal(2UL,f.A.PredictedTick);
                f.Server.Poll(TimeSpan.FromMilliseconds(200));f.A.Poll();
                Assert.Equal(2UL,f.A.RollbackCount);Assert.Equal(1UL,f.A.LastRestoreTick);
                Drain(f);Assert.Equal(10L,f.A.InspectPredictedState());
                Assert.Equal(2UL,f.A.ConfirmedTick);Assert.True(!f.A.IsReplaying);
            });
            yield return ("prediction.rejects_insufficient_snapshot_budget", () => {
                Assert.Throws<ArgumentException>(() => new SyncFixture(prediction: new PredictionOptions(maxRetainedBytes: 1024)));
            });
        }
        // 测试宿主已完成采样：逐帧冻结“无操作”。这不是框架替本地玩家预测输入。
        private static void FreezeLocal(SyncFixture f, ulong first, ulong last)
        { for (ulong tick = first; tick <= last; tick++) Assert.True(f.A.TrySubmit(tick, new byte[] {0})); }

        private static void Drain(SyncFixture f)
        { for (int i = 0; i < 32; i++) { f.A.Poll(); f.B.Poll(); } }
    }
}
