using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.CombatSpacingTests;

internal static partial class Program
{
    private static readonly Guid SessionId=new("1183a723-34d0-4f52-8341-fc6373f2c6bf");
    private static readonly HookGame Codec=new(false);
    private static IslandMap Map(CrabConfig? crabs=null,EnemyConfig? enemies=null,MerchantConfig? merchants=null)
    {
        var m=IslandMap.CreatePlatform();
        return new IslandMap(m.Shore,m.Pier,m.Spawns,m.FishSpots,m.Platform,
            new FishingConfig(melee:new MeleeConfig(),crabs:crabs??CrabConfig.Disabled,enemies:enemies??EnemyConfig.Disabled,merchants:merchants));
    }
    private static IDeterministicSimulation<HookState> Model(IslandMap? map=null)
    {
        var model=Codec.Create();
        model.Initialize(new SimulationInitialization(new SessionDescriptor(SessionId,1,30,
            Enumerable.Range(0,4).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),new ByteString((map??Map()).Encode())));
        return model;
    }
    private static void Step(IDeterministicSimulation<HookState> model,params HookInput[] inputs)
    {
        var s=model.Capture();
        model.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select(p=>new SimulationInput(
            new Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new ByteString((p.Slot<inputs.Length?inputs[p.Slot]:default).Encode()))).ToArray());
    }
    private static HookState Replace(HookState s,Fisher[]? players=null,CrabState[]? crabs=null,EnemyState? enemies=null,
        IEnumerable<PlatformTile>? tiles=null)=>new(s.Steps,players??s.Players.ToArray(),s.Fish,s.Coins,tiles??s.Tiles,s.Driftwood,s.Tackle,
            enemies??s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,crabs??s.Crabs.ToArray(),s.Boss,s.Grenades);
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Separate(IReadOnlyList<Point> positions,int distance)
    {
        for(int i=0;i<positions.Count;i++)for(int j=i+1;j<positions.Count;j++)
            Require(IslandMap.DistanceSquared(positions[i].X,positions[i].Z,positions[j].X,positions[j].Z)>=(long)(distance-2)*(distance-2),
                $"Bodies {i}/{j} overlap: {positions[i].X},{positions[i].Z} / {positions[j].X},{positions[j].Z}; required={distance}");
    }
    private static IEnumerable<(string,Action)> GameCases()
    {
        yield return ("coincident_players_separate_without_walking",()=>{
            var model=Model();var state=model.Capture();var people=state.Players.ToArray();
            for(int i=0;i<people.Length;i++){people[i].X=-1700;people[i].Z=-1600;}
            model.Restore(Replace(state,people));Step(model);
            Separate(model.Capture().Players.Select(p=>new Point(p.X,p.Z)).ToArray(),EnemyGeometry.PlayerRadius*2);
            Require(model.Capture().Players.All(p=>!p.Swimming.InWater),"Spacing pushed a player into water");
        });
        yield return ("shore_crowd_stays_on_ground_and_rollback_is_identical",()=>{
            var model=Model();var state=model.Capture();var people=state.Players.ToArray();
            for(int i=0;i<people.Length;i++){people[i].X=-2790;people[i].Z=-2400;}
            model.Restore(Replace(state,people));var restored=Model();
            for(int tick=0;tick<30;tick++)
            {
                restored.Restore(Codec.Decode(Codec.Encode(model.Capture())));Step(model);Step(restored);
                Require(Codec.Encode(model.Capture()).SequenceEqual(Codec.Encode(restored.Capture())),"Restored spacing cache changed state bytes");
                Require(model.Capture().Players.All(p=>!p.Swimming.InWater && PlatformRules.CanStand(model.Capture().Tiles,p.X,p.Z)),"Crowd crossed the shoreline");
            }
            Separate(model.Capture().Players.Select(p=>new Point(p.X,p.Z)).ToArray(),EnemyGeometry.PlayerRadius*2);
        });
        yield return ("crabs_separate_from_players_without_rotating_locked_pinch",()=>{
            var map=Map(new CrabConfig(initialDelayTicks:108000,count:3));var model=Model(map);var s=model.Capture();
            var crabs=Enumerable.Range(0,3).Select(_=>new CrabState{Generation=1,Phase=CrabPhase.Windup,Timer=18,Health=100,
                X=-1400,Z=-1400,DirectionZ=EnemyGeometry.DirectionScale,AttackSerial=1,Target=0}).ToArray();
            model.Restore(Replace(s,crabs:crabs));Step(model);s=model.Capture();
            Separate(s.Crabs.Select(c=>new Point(c.X,c.Z)).ToArray(),CrabConfig.BodyRadius*2);
            foreach(var c in s.Crabs)
            {
                Require(c.Phase==CrabPhase.Windup && c.DirectionX==0 && c.DirectionZ==EnemyGeometry.DirectionScale && c.AttackSerial==1,"Spacing changed locked attack direction or receipt");
                foreach(var p in s.Players)Require(IslandMap.DistanceSquared(c.X,c.Z,p.X,p.Z)>=728L*728,"Crab overlaps a player");
            }
        });
        yield return ("free_piranhas_separate_in_water_from_swimming_player",()=>{
            var map=Map(enemies:new EnemyConfig(piranhaCount:3,monsterEnabled:false));var model=Model(map);var s=model.Capture();
            var fish=s.Enemies.Piranhas.ToArray();
            for(int i=0;i<fish.Length;i++){fish[i].X=7000;fish[i].Z=-7000;fish[i].DamageTaken=1;fish[i].StunTicks=15;}
            var people=s.Players.ToArray();people[0].X=7000;people[0].Z=-7000;people[0].Swimming.InWater=true;
            model.Restore(Replace(s,people,enemies:new EnemyState(fish,s.Enemies.Monster,s.Enemies.Waterballs)));Step(model);s=model.Capture();
            Separate(s.Enemies.Piranhas.Select(f=>new Point(f.X,f.Z)).ToArray(),500);
            foreach(var f in s.Enemies.Piranhas)Require(IslandMap.DistanceSquared(f.X,f.Z,s.Players[0].X,s.Players[0].Z)>=578L*578,"Piranha overlaps swimmer");
            Require(s.Players[0].Swimming.InWater,"Fish pushed swimmer onto the platform");
        });
        yield return ("fish_beneath_deck_does_not_displace_ground_player",()=>{
            var map=Map(enemies:new EnemyConfig(piranhaCount:1,monsterEnabled:false));var model=Model(map);var s=model.Capture();
            var fish=s.Enemies.Piranhas.ToArray();fish[0].X=s.Players[0].X;fish[0].Z=s.Players[0].Z;fish[0].DamageTaken=1;fish[0].StunTicks=15;
            model.Restore(Replace(s,enemies:new EnemyState(fish,s.Enemies.Monster,s.Enemies.Waterballs)));Step(model);
            Require(model.Capture().Players[0].X==s.Players[0].X && model.Capture().Players[0].Z==s.Players[0].Z,"Underwater fish collided through decking");
        });
    }
    private static int Main(string[] args)
    {
        int failed=0,passed=0;
        foreach(var (name,test) in GameCases().Concat(BoundaryCases()).Concat(AlgorithmCases()).Concat(NetworkCases()).Where(c=>args.Length==0 || args.Any(a=>c.Item1.Contains(a,StringComparison.OrdinalIgnoreCase))))
        {
            try{test();Console.WriteLine("PASS "+name);passed++;}
            catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e);}
        }
        Console.WriteLine($"Combat spacing: {passed} passed, {failed} failed");return failed==0 && passed>0?0:1;
    }
}
