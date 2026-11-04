using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.CombatSpacingTests;

internal static partial class Program
{
    private static IEnumerable<(string,Action)> AlgorithmCases()
    {
        yield return ("algorithm_input_order_and_warmed_cache_do_not_affect_result",()=>{
            var source=Enumerable.Range(0,12).Select(i=>new CombatBody(i,-2000+i%3*250,-2000+i/3*250,330,1)).ToList();
            var forward=source.ToList();var backward=source.AsEnumerable().Reverse().ToList();var solver=new CombatSpacing();
            solver.Resolve(forward,Free);solver.Resolve(backward,Free);
            Require(forward.Select(Position).SequenceEqual(backward.Select(Position)),"Input order or previous grid changed spacing");
            Separate(forward.Select(b=>new Point(b.X,b.Z)).ToArray(),660);
        });
        yield return ("algorithm_fixed_body_and_disjoint_layers_are_preserved",()=>{
            var bodies=new List<CombatBody>{new CombatBody(100,0,0,900,3,true),new CombatBody(2,0,0,400,1),
                new CombatBody(3,0,0,250,2),new CombatBody(4,-3000,0,330,1),new CombatBody(5,-3000,0,250,2)};
            new CombatSpacing().Resolve(bodies,Free);
            var anchor=bodies.Single(b=>b.Id==100);Require(anchor.X==0 && anchor.Z==0,"Fixed combat body moved");
            foreach(var b in bodies.Where(b=>b.Id==2 || b.Id==3))Require(IslandMap.DistanceSquared(0,0,b.X,b.Z)>=(long)(900+b.Radius)*(900+b.Radius),"Body remains inside fixed unit");
            Require(bodies.Where(b=>b.Id==4 || b.Id==5).All(b=>b.X==-3000 && b.Z==0),"Collision crossed ground/water layers");
        });
        yield return ("algorithm_blocked_side_transfers_correction_without_crossing_wall",()=>{
            var bodies=new List<CombatBody>{new CombatBody(0,-150,-100,330,1),new CombatBody(1,-150,-100,330,1)};
            new CombatSpacing().Resolve(bodies,(_,from,to)=>to.X<=0 && to.Z<=0);
            Require(bodies.All(b=>b.X<=0 && b.Z<=0),"Spacing crossed a wall");Separate(bodies.Select(b=>new Point(b.X,b.Z)).ToArray(),660);
        });
        yield return ("algorithm_sparse_world_uses_nearby_candidates",()=>{
            var bodies=Enumerable.Range(0,1600).Select(i=>new CombatBody(i,-48000+i%40*2400,-48000+i/40*2400,330,1)).ToList();
            int checks=new CombatSpacing().Resolve(bodies,Free);
            Require(checks<1600*4,$"Spatial partition regressed to broad all-pairs checks: {checks}");
            Console.WriteLine($"SPACING_GRID bodies={bodies.Count} narrow_candidates={checks} all_pairs={bodies.Count*(bodies.Count-1)/2}");
        });
        yield return ("algorithm_path_query_cannot_jump_across_thin_wall",()=>{
            var bodies=new List<CombatBody>{new CombatBody(0,0,0,330,1,true),new CombatBody(1,100,0,330,1)};
            bool Path(CombatBody _,Point from,Point to)=>!(Math.Min(from.X,to.X)<250 && Math.Max(from.X,to.X)>240);
            new CombatSpacing().Resolve(bodies,Path);
            Require(bodies.Single(b=>b.Id==1).X<=240,"Solver tested only destination and jumped across a wall");
        });
        yield return ("algorithm_crowd_can_slide_along_a_narrow_corridor",()=>{
            var bodies=new List<CombatBody>{new CombatBody(0,0,0,330,1),new CombatBody(2,0,0,330,1)};
            new CombatSpacing().Resolve(bodies,(_,from,to)=>Math.Abs(to.X)<=10);
            Require(bodies.All(b=>Math.Abs(b.X)<=10),"Corridor separation crossed a wall");
            Separate(bodies.Select(b=>new Point(b.X,b.Z)).ToArray(),660);
        });
    }
    private static bool Free(CombatBody _,Point from,Point to)=>true;
    private static (int,int,int) Position(CombatBody b)=>(b.Id,b.X,b.Z);
}
