using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Simulation;

public sealed class HookGame : IStateCodec<HookState>, ISimulationFactory<HookState>, IInputValidator
{
    public const int Rate=30, Range=14000, JumpTicks=90;
    public static SimulationModule<HookState> CreateModule()
    {
        var game=new HookGame();
        return new SimulationModule<HookState>(new CompatibilityManifest("hook-island",rulesVersion:"1",inputFormatVersion:"1",
            stateFormatVersion:"1",contentHash:"fish-10-20-30-shared-wallet-v1",determinismHash:"integer-mm-hook-v1"),game,game,game);
    }
    public IDeterministicSimulation<HookState> Create()=>new Model();
    public void ValidateCanonical(ReadOnlySpan<byte> bytes)=>HookInput.Decode(bytes);
    public byte[] Encode(HookState s)
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        w.Write(0x31534948);w.Write(s.Steps);w.Write(s.Coins);w.Write(s.Players.Count);
        foreach(var p in s.Players)
        {
            w.Write(p.Slot);w.Write(p.X);w.Write(p.Z);w.Write(p.AimX);w.Write(p.AimZ);w.Write(p.RemainderX);w.Write(p.RemainderZ);
            w.Write((byte)p.Phase);w.Write(p.HookX);w.Write(p.HookZ);w.Write(p.DirectionX);w.Write(p.DirectionZ);w.Write(p.Distance);
            w.Write(p.FishId);w.Write(p.Cooldown);w.Write(p.Catches);w.Write(p.Coins);w.Write(p.Casts);
        }
        w.Write(s.Fish.Count);
        foreach(var f in s.Fish){w.Write(f.Id);w.Write(f.X);w.Write(f.Z);w.Write(f.Y);w.Write(f.Timer);w.Write(f.Owner);w.Write((byte)f.Phase);}
        return stream.ToArray();
    }
    public HookState Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<24 || bytes.Length>4096)throw new InvalidDataException("Invalid state length.");
        using var r=new BinaryReader(new MemoryStream(bytes.ToArray(),false));
        if(r.ReadInt32()!=0x31534948)throw new InvalidDataException("State version mismatch.");
        ulong tick=r.ReadUInt64();int coins=r.ReadInt32(),count=r.ReadInt32();
        if(count<1 || count>4 || coins<0)throw new InvalidDataException("Invalid players/wallet.");
        var players=new Fisher[count];
        for(int i=0;i<count;i++)
        {
            var p=new Fisher { Slot=r.ReadUInt16(),X=r.ReadInt32(),Z=r.ReadInt32(),AimX=r.ReadInt32(),AimZ=r.ReadInt32(),
                RemainderX=r.ReadInt32(),RemainderZ=r.ReadInt32(),Phase=(HookPhase)r.ReadByte(),HookX=r.ReadInt32(),HookZ=r.ReadInt32(),
                DirectionX=r.ReadInt32(),DirectionZ=r.ReadInt32(),Distance=r.ReadInt32(),FishId=r.ReadInt32(),Cooldown=r.ReadInt32(),
                Catches=r.ReadInt32(),Coins=r.ReadInt32(),Casts=r.ReadInt32() };
            if(p.Slot!=i || p.Phase>HookPhase.Returning || p.FishId< -1 || p.FishId>31 || p.Cooldown<0 || p.Cooldown>12 ||
                p.Catches<0 || p.Coins<0 || p.Casts<0 || Math.Abs((long)p.X)>50000 || Math.Abs((long)p.Z)>50000 ||
                Math.Abs((long)p.HookX)>100000 || Math.Abs((long)p.HookZ)>100000 ||
                Math.Abs(p.DirectionX)>1000 || Math.Abs(p.DirectionZ)>1000 || p.Distance<0 || p.Distance>Range+600 ||
                Math.Abs((long)p.AimX)>100000 || Math.Abs((long)p.AimZ)>100000 ||
                Math.Abs((long)p.RemainderX)>=1000 || Math.Abs((long)p.RemainderZ)>=1000)
                throw new InvalidDataException("Invalid fisher.");
            players[i]=p;
        }
        int fishCount=r.ReadInt32();if(fishCount<1 || fishCount>32)throw new InvalidDataException("Invalid fish count.");
        var fish=new Fish[fishCount];
        for(int i=0;i<fishCount;i++)
        {
            var f=new Fish { Id=r.ReadInt32(),X=r.ReadInt32(),Z=r.ReadInt32(),Y=r.ReadInt32(),Timer=r.ReadInt32(),Owner=r.ReadInt32(),Phase=(FishPhase)r.ReadByte() };
            if(f.Id!=i || f.Phase>FishPhase.Hooked || f.Owner< -1 || f.Owner>=count || f.Timer<0 || f.Timer>300 ||
                Math.Abs((long)f.X)>100000 || Math.Abs((long)f.Z)>100000 || f.Y<0 || f.Y>5000)
                throw new InvalidDataException("Invalid fish.");
            fish[i]=f;
        }
        if(r.BaseStream.Position!=bytes.Length || players.Sum(p=>(long)p.Coins)!=coins)throw new InvalidDataException("Invalid wallet/state tail.");
        foreach(var p in players)
            if(p.FishId>=fishCount || (p.FishId>=0 && (p.Phase!=HookPhase.Returning || fish[p.FishId].Owner!=p.Slot)))
                throw new InvalidDataException("Invalid hook ownership.");
        foreach(var f in fish)
            if((f.Phase==FishPhase.Hooked)!=(f.Owner>=0) || (f.Owner>=0 && players[f.Owner].FishId!=f.Id))
                throw new InvalidDataException("Invalid fish ownership.");
        return new HookState(tick,players,fish,coins);
    }
    public static int Sqrt(long n)
    {
        if(n<=0)return 0;
        long x=n,y=(x+1)/2;
        while(y<x){x=y;y=(x+n/x)/2;}
        return (int)x;
    }
    /// <summary>Swept-circle query: fast hooks cannot skip a fish between frames.</summary>
    public static bool SegmentHit(int ax,int az,int bx,int bz,int x,int z,int radius)
    {
        long dx=bx-ax,dz=bz-az,len=dx*dx+dz*dz;
        if(len==0)return IslandMap.DistanceSquared(ax,az,x,z)<=(long)radius*radius;
        long t=Math.Max(0,Math.Min(len,(x-ax)*dx+(z-az)*dz));
        long px=ax+dx*t/len,pz=az+dz*t/len;
        return (x-px)*(x-px)+(z-pz)*(z-pz)<=(long)radius*radius;
    }
    public static int Value(int id)=>id%7==6?30:id%3==2?20:10;

    private sealed class Model : IDeterministicSimulation<HookState>
    {
        private IslandMap map=null!;
        private Fisher[] players=Array.Empty<Fisher>();
        private Fish[] fish=Array.Empty<Fish>();
        private ulong tick;
        private int coins;
        public void Initialize(SimulationInitialization init)
        {
            if(init.Session.TickRate!=Rate)throw new InvalidDataException("Hook Island requires 30 Hz.");
            map=IslandMap.Decode(init.Data.Span);
            players=init.Session.Participants.Select(p=>{
                if(p.Slot>=4)throw new InvalidDataException("Invalid slot.");
                var s=map.Spawns[p.Slot];if(!map.CanStand(s.X,s.Z))throw new InvalidDataException("Spawn outside walkable island.");
                return new Fisher { Slot=p.Slot,X=s.X,Z=s.Z,AimX=s.X,AimZ=s.Z-5000,FishId=-1 };
            }).ToArray();
            fish=map.FishSpots.Select((p,i)=>new Fish { Id=i,X=p.X,Z=p.Z,Owner=-1,Timer=i*7,Phase=FishPhase.Waiting }).ToArray();
            tick=0;coins=0;
        }
        public IReadOnlyList<ByteString> Step(FrameContext context,IReadOnlyList<SimulationInput> inputs)
        {
            UpdateFish();
            var commands=new HookInput[players.Length];
            foreach(var input in inputs)commands[input.Participant.Slot]=HookInput.Decode(input.Payload.Span);
            // Slots are ordered. Simultaneous claims have a stable winner and a single wallet transaction.
            for(int i=0;i<players.Length;i++)
            {
                var p=players[i];var c=commands[i];
                int nx=c.MoveX*180+p.RemainderX,nz=c.MoveZ*180+p.RemainderZ;
                int x=p.X+nx/1000,z=p.Z+nz/1000;
                if(map.CanStand(x,p.Z)){p.X=x;p.RemainderX=nx%1000;}else p.RemainderX=0;
                if(map.CanStand(p.X,z)){p.Z=z;p.RemainderZ=nz%1000;}else p.RemainderZ=0;
                if(c.AimX!=0 || c.AimZ!=0 || c.Fire){p.AimX=c.AimX;p.AimZ=c.AimZ;}
                if(p.Cooldown>0)p.Cooldown--;
                if(c.Cancel && p.Phase==HookPhase.Flying)p.Phase=HookPhase.Returning;
                if(c.Fire && p.Phase==HookPhase.Ready && p.Cooldown==0)
                {
                    int dx=c.AimX-p.X,dz=c.AimZ-p.Z,length=Sqrt((long)dx*dx+(long)dz*dz);
                    if(length>100)
                    {
                        p.DirectionX=dx*1000/length;p.DirectionZ=dz*1000/length;
                        p.HookX=p.X;p.HookZ=p.Z;p.Phase=HookPhase.Flying;p.Distance=0;p.Casts++;
                    }
                }
                UpdateHook(ref p);players[i]=p;
            }
            tick++;
            return Array.Empty<ByteString>();
        }
        private void UpdateFish()
        {
            for(int i=0;i<fish.Length;i++)
            {
                var f=fish[i];if(f.Phase==FishPhase.Hooked)continue;
                var spot=map.FishSpots[i];f.X=spot.X;f.Z=spot.Z;
                if(f.Phase==FishPhase.Waiting)
                { if(f.Timer>0)f.Timer--;else{f.Phase=FishPhase.Warning;f.Timer=24;}f.Y=0; }
                else if(f.Phase==FishPhase.Warning)
                { if(--f.Timer<=0){f.Phase=FishPhase.Jumping;f.Timer=0;} }
                else
                {
                    f.Timer++;
                    if(f.Timer>=JumpTicks){f.Phase=FishPhase.Waiting;f.Timer=35+i%5*7;f.Y=0;}
                    else
                    {
                        f.Y=4*2400*f.Timer*(JumpTicks-f.Timer)/(JumpTicks*JumpTicks);
                        // Short horizontal leap, predictable during the warning circle.
                        f.X=spot.X+(f.Timer-JumpTicks/2)*(i%2==0?12:-12);
                    }
                }
                fish[i]=f;
            }
        }
        private void UpdateHook(ref Fisher p)
        {
            if(p.Phase==HookPhase.Flying)
            {
                int oldX=p.HookX,oldZ=p.HookZ;
                p.HookX+=p.DirectionX*600/1000;p.HookZ+=p.DirectionZ*600/1000;p.Distance+=600;
                if(IslandMap.Blocked(p.HookX,p.HookZ,100)){p.Phase=HookPhase.Returning;return;}
                for(int i=0;i<fish.Length;i++)
                {
                    var f=fish[i];
                    if(f.Phase!=FishPhase.Jumping || f.Y<300 || !SegmentHit(oldX,oldZ,p.HookX,p.HookZ,f.X,f.Z,720))continue;
                    f.Phase=FishPhase.Hooked;f.Owner=p.Slot;fish[i]=f;
                    p.HookX=f.X;p.HookZ=f.Z;p.FishId=i;p.Phase=HookPhase.Returning;break;
                }
                if(p.Distance>=Range)p.Phase=HookPhase.Returning;
            }
            else if(p.Phase==HookPhase.Returning)
            {
                int dx=p.X-p.HookX,dz=p.Z-p.HookZ,length=Sqrt((long)dx*dx+(long)dz*dz);
                if(length<=800)
                {
                    if(p.FishId>=0)
                    {
                        int value=Value(p.FishId);coins=checked(coins+value);p.Coins=checked(p.Coins+value);p.Catches++;
                        var f=fish[p.FishId];f.Phase=FishPhase.Waiting;f.Owner=-1;f.Timer=100;f.Y=0;fish[p.FishId]=f;
                    }
                    p.FishId=-1;p.Phase=HookPhase.Ready;p.Cooldown=6;p.HookX=p.X;p.HookZ=p.Z;
                }
                else
                {
                    p.HookX+=dx*800/length;p.HookZ+=dz*800/length;
                    if(p.FishId>=0){var f=fish[p.FishId];f.X=p.HookX;f.Z=p.HookZ;f.Y=1250;fish[p.FishId]=f;}
                }
            }
        }
        public HookState Capture()=>new HookState(tick,players,fish,coins);
        public void Restore(HookState state)
        {
            // Validate ownership as well as positions before using a received checkpoint.
            state=new HookGame().Decode(new HookGame().Encode(state));
            if(state.Players.Count!=players.Length || state.Fish.Count!=fish.Length || state.Players.Any(p=>!map.CanStand(p.X,p.Z)))
                throw new InvalidDataException("State does not match island.");
            tick=state.Steps;coins=state.Coins;players=state.Players.ToArray();fish=state.Fish.ToArray();
        }
    }
}
