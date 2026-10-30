using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Persistence;
using FrameSync.Runtime;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static byte[] SaveKey(int value)=>Enumerable.Repeat((byte)value,32).ToArray();
    private static IslandSave SaveFixture()
    {
        var runner=Runner(4,RecoveryMap());
        for(int i=0;i<12;i++)Step(runner,new HookInput(0,0,0,-12000,i==10));
        return new IslandSave(new SaveDocument(Guid.NewGuid(),runner.CaptureCheckpoint()),
            new DateTime(2026,10,29,12,0,0,DateTimeKind.Utc),Enumerable.Range(1,4).Select(SaveKey).ToArray(),new[]{true,true,true,true});
    }
    private static IEnumerable<(string,Action)> SaveCases()
    {
        yield return ("save_safety_waits_for_hooks_rescues_and_melee_without_mutating_world",()=>{
            var state=Runner(4,RecoveryMap()).InspectState();Require(IslandSaveSafety.IsSafe(state),"An idle party cannot save.");
            void Block(Fisher player)
            {var players=state.Players.ToArray();players[2]=player;Require(!IslandSaveSafety.IsSafe(new HookState(state.Steps,players,state.Fish,state.Coins,state.Tiles,state.Driftwood,state.Tackle)),"An unresolved action was considered safe.");}
            var p=state.Players[2];p.Phase=HookPhase.Flying;Block(p);
            p=state.Players[2];p.PulledBy=1;Block(p);
            p=state.Players[2];p.HookedPlayer=1;Block(p);
            p=state.Players[2];p.Revival.Rescuer=1;p.Revival.RescueTicks=1;Block(p);
            p=state.Players[2];p.FishId=0;Block(p);
            p=state.Players[2];p.HookedPiranha=1;Block(p);
            p=state.Players[2];p.HookedChest=1;Block(p);
            p=state.Players[2];p.HookedFinale=true;Block(p);
            p=state.Players[2];p.Melee.RemainingTicks=1;Block(p);
            p=state.Players[2];p.Knockback.Active=true;Block(p);
            Require(IslandSaveSafety.IsSafe(state),"Eligibility changed the world.");
        });
        yield return ("save_paid_purchase_receipt_and_combo_survive_restart_without_double_charge",()=>{
            var map=RecoveryMap();var runner=Runner(1,map);var pilot=new Advanture.HookIsland.RodSmokePilot();
            for(int i=0;i<12000;i++)
            {var state=runner.InspectState();if(state.Players[0].LastRodResult==RodResult.Success && IslandSaveSafety.IsSafe(state))break;Step(runner,pilot.Sample(map,state,0,runner.Tick+1));}
            var before=runner.InspectState();var player=before.Players[0];Require(player.LastRodResult==RodResult.Success,"Fixture never bought a rod through normal play.");
            var save=new IslandSave(new SaveDocument(Guid.NewGuid(),runner.CaptureCheckpoint()),DateTime.UtcNow,new[]{SaveKey(1)},new[]{true});
            var disk=new IslandSaveStore(new SaveMemoryStore());disk.Save(save);
            var initial=CheckpointRestart.Create(disk.Load().Document.Checkpoint,Guid.NewGuid());
            var restored=new SimulationRunner<HookState>(HookGame.CreateModule(),initial.Session,initial.Limits,initial.InitializationData);restored.Restore(initial);
            for(ulong tick=1;tick<=5;tick++)
            {
                var member=initial.Session.Participants[0];var command=new HookInput(0,0,shopping:true,rodAction:RodAction.BuyAndEquip,rodTarget:0,rodBatch:before.Tackle!.Batch,rodRequestId:player.LastRodRequest);
                var events=tick==1?new[]{new MemberEvent(1,MemberEventKind.Activate,0,member.Generation)}:Array.Empty<MemberEvent>();
                restored.Advance(new InputFrame(initial.Session.SessionId,initial.Session.Epoch,tick,new[]{new RecordedInput(0,member.Generation,command.Encode())},events));
            }
            var after=restored.InspectState();var current=after.Players[0];
            Require(current.Coins==player.Coins && current.EquippedRod==player.EquippedRod && current.LastRodRequest==player.LastRodRequest && current.Combo.Count==player.Combo.Count,"Restore replayed a purchase, lost equipment or reset combo.");
            Require(before.Tackle!.Inventory.Items.SequenceEqual(after.Tackle!.Inventory.Items),"Restore duplicated or lost inventory items.");
        });
        yield return ("save_file_roundtrip_keeps_full_midcast_checkpoint",()=>{
            string directory=Path.Combine("artifacts/game-saves/tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try
            {
                var store=new IslandSaveStore(Path.Combine(directory,"manual.sav"));var save=SaveFixture();store.Save(save);var loaded=store.Load();
                Require(CheckpointCodec.Encode(save.Document.Checkpoint).SequenceEqual(CheckpointCodec.Encode(loaded.Document.Checkpoint)),"Saved checkpoint changed.");
                Require(loaded.Document.SaveId==save.Document.SaveId && loaded.SavedAtUtc==save.SavedAtUtc && loaded.Capacity==4,"Save identity/metadata changed.");
                Require(loaded.Profiles.Zip(save.Profiles,(a,b)=>a.SequenceEqual(b)).All(x=>x),"Seat ownership changed.");
                store.Save(save);Require(store.Load().Document.SaveId==save.Document.SaveId,"Atomic replacement failed.");
            }
            finally{Directory.Delete(directory,true);}
        });
        yield return ("save_corruption_truncation_and_trailing_bytes_are_rejected",()=>{
            var bytes=IslandSaveCodec.Encode(SaveFixture());
            foreach(int offset in new[]{0,7,20,bytes.Length-1})
            {var bad=(byte[])bytes.Clone();bad[offset]^=1;ExpectBadSave(bad);}
            ExpectBadSave(bytes.Take(bytes.Length-1).ToArray());ExpectBadSave(bytes.Concat(new byte[]{0}).ToArray());
            ExpectBadSave(new byte[IslandSaveCodec.MaximumBytes+1]);
        });
        yield return ("save_failed_commit_retains_previous_archive",()=>{
            var io=new SaveMemoryStore();var store=new IslandSaveStore(io);var first=SaveFixture();store.Save(first);
            var before=(byte[])io.Bytes.Clone();io.Fail=true;bool failed=false;
            try{store.Save(SaveFixture());}catch(IOException){failed=true;}
            Require(failed && before.SequenceEqual(io.Bytes) && store.Load().Document.SaveId==first.Document.SaveId,"Failed write replaced the previous archive.");
        });
        yield return ("save_profile_file_survives_new_process_adapter",()=>{
            string directory=Path.Combine("artifacts/game-saves/tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try
            {
                string path=Path.Combine(directory,"player.identity");var a=IslandPlayerIdentity.LoadOrCreate(path);var b=IslandPlayerIdentity.LoadOrCreate(path);
                Require(a.Length==32 && a.SequenceEqual(b),"Local ownership proof changed.");
                File.WriteAllBytes(path,new byte[]{1});bool rejected=false;
                try{IslandPlayerIdentity.LoadOrCreate(path);}catch(InvalidDataException){rejected=true;}
                Require(rejected && File.ReadAllBytes(path).Length==1,"Corrupt identity was silently replaced, losing saved ownership.");
            }
            finally{Directory.Delete(directory,true);}
        });
    }
    private static void ExpectBadSave(byte[] bytes)
    {bool failed=false;try{IslandSaveCodec.Decode(bytes);}catch(InvalidDataException){failed=true;}Require(failed,"Invalid archive was accepted.");}
    private sealed class SaveMemoryStore:ISaveStore
    {
        internal byte[] Bytes=Array.Empty<byte>();internal bool Fail;
        public byte[] Read(int maxBytes)=>Bytes.Length<=maxBytes?(byte[])Bytes.Clone():throw new InvalidDataException();
        public void WriteAtomic(ReadOnlySpan<byte> bytes,int maxBytes){if(Fail)throw new IOException("Storage unavailable.");Bytes=bytes.ToArray();}
    }
}
