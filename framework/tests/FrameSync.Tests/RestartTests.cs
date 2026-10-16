using System;
using System.Collections.Generic;
using System.IO;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;
using FrameSync.Transport;

namespace FrameSync.Tests
{
    internal static class RestartTests
    {
        public static IEnumerable<(string, Action)> Cases()
        {
            yield return ("restart.new_identity_resets_session_tick_and_preserves_world_time", () => {
                var old = Fixture.Runner(); old.Advance(Fixture.Frame(1, Fixture.Input(0,1), Fixture.Input(1,2)));
                var saved = old.CaptureCheckpoint(); var start = CheckpointRestart.Create(saved, Guid.NewGuid());
                Assert.Equal(0UL, start.Tick); Assert.Equal(1UL, start.Session.BaseTick);
                Assert.Equal(0, start.InputHistory.Count); Assert.Equal(0UL, start.Membership.LastSequence);
                Assert.Bytes(saved.ApplicationState.ToArray(), start.ApplicationState.ToArray());
                Assert.True(!start.Membership.Find(0).IsActive); Assert.Equal(2UL, start.Membership.Find(0).Generation);
                var runner = new SimulationRunner<TestState>(new TestModule().Module(), start.Session, start.Limits, start.InitializationData);
                runner.Restore(start);
                Assert.Throws<ArgumentException>(() => runner.Advance(Fixture.Frame(2, Fixture.Input(0,3), Fixture.Input(1,4))));
                runner.Advance(new InputFrame(start.Session.SessionId, start.Session.Epoch, 1,
                    new[] {new RecordedInput(0,2,new byte[]{3}), new RecordedInput(1,2,new byte[]{4})},
                    new[] {new MemberEvent(1,MemberEventKind.Activate,0,2),new MemberEvent(2,MemberEventKind.Activate,1,2)}));
                Assert.Equal(1234L, runner.InspectState().Value); Assert.Equal(2UL, runner.InspectState().LastPersistentTick);
                Assert.Throws<ArgumentException>(() => CheckpointRestart.Create(saved, saved.Session.SessionId));
            });
            yield return ("restart.loaded_initial_state_reaches_clients_and_old_packets_are_ignored", () => {
                var f = new SyncFixture(); f.Start(); f.A.TrySubmit(1,new byte[]{5}); f.Accept(); f.Finish(1);
                var initial = CheckpointRestart.Create(f.Server.CaptureCheckpoint(),Guid.NewGuid());
                var network = new InMemoryNetwork(); var st=network.CreateEndpoint(1); var at=network.CreateEndpoint(2); var bt=network.CreateEndpoint(3);
                var server = new ServerSession<long>(new SyncTestGame().Module(),initial.Session,st,
                    new[]{new PeerBinding(2,0),new PeerBinding(3,1)}, _=>new ByteString(new byte[]{0}), initialCheckpoint:initial);
                var a = new ClientSession<long>(new SyncTestGame().Module(),initial.Session.SessionId,1,at,1);
                var b = new ClientSession<long>(new SyncTestGame().Module(),initial.Session.SessionId,1,bt,1);
                for(int i=0;i<20;i++){server.Poll(TimeSpan.Zero);a.Poll();b.Poll();}
                Assert.Equal(5L,b.InspectState());
                SessionBoundaryTests.Send(at,1,MessageKind.Input,Wire.Input(Fixture.SessionId,1,1,Fixture.Input(0,9),900));
                SessionBoundaryTests.Send(st,3,MessageKind.Final,Wire.Final(f.Server.LastFrame!,CheckpointCodec.Hash(f.Server.CaptureCheckpoint()),900));
                a.TrySubmit(1,new byte[]{3});a.Poll();server.Poll(TimeSpan.Zero);server.Poll(TimeSpan.FromMilliseconds(100));a.Poll();b.Poll();
                Assert.Equal(8L,b.InspectState()); Assert.Equal(1UL,server.RejectedMessages); Assert.Equal(1UL,b.RejectedMessages);
                Assert.Bytes(CheckpointCodec.Encode(server.CaptureCheckpoint()),CheckpointCodec.Encode(b.CaptureCheckpoint()));
            });
            yield return ("restart.save_failure_freezes_confirmed_state_and_retry_closes", () => {
                var f = new SyncFixture(); f.Start(); f.Finish(1); var before=CheckpointCodec.Encode(f.Server.CaptureCheckpoint());
                Assert.Throws<IOException>(()=>f.Server.SaveAndClose(_=>throw new IOException("disk full")));
                Assert.Equal(SessionStatus.Saving,f.Server.Status);
                f.Server.Poll(TimeSpan.FromSeconds(1)); Assert.Equal(1UL,f.Server.ConfirmedTick);
                Assert.Equal(SessionStatus.Running,f.A.Status);
                int saves=0; f.Server.SaveAndClose(c=>{Assert.Bytes(before,CheckpointCodec.Encode(c));saves++;});
                f.Server.Poll(TimeSpan.FromSeconds(1)); f.A.Poll(); f.B.Poll();
                Assert.Equal(1,saves);Assert.Equal(SessionStatus.Closed,f.B.Status);f.AssertEqual(1,0);
            });
            yield return ("scheduler.fixed_lead_uses_elapsed_time_without_advancing_simulation", () => {
                var clock=new FixedInputScheduler(30);clock.Start(TimeSpan.FromSeconds(10));
                Assert.Equal(3UL,clock.InputTick(TimeSpan.FromSeconds(10),0));
                Assert.Equal(33UL,clock.InputTick(TimeSpan.FromSeconds(11),0));
                Assert.Equal(36UL,clock.InputTick(TimeSpan.FromSeconds(11),0,35));
                clock.ObserveConfirmed(TimeSpan.FromSeconds(11),40);
                Assert.Equal(43UL,clock.InputTick(TimeSpan.FromSeconds(11),40));
                Assert.Throws<ArgumentOutOfRangeException>(()=>clock.InputTick(TimeSpan.Zero,0));
            });
        }
    }
}
