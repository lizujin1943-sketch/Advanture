using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.BossTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> TerrainValidationCases()
    {
        yield return ("arena_cache_preserves_merchant_holes_and_independent_results",()=>{
            var baseline=BossArena.InitialTiles();
            var config=new FishingConfig(boss:new BossConfig(),merchants:new MerchantConfig(new[]{new MerchantDefinition(1,2,0,7)}));
            var merchantMap=BossArena.InitialTiles(config);
            Require(baseline.Any(t=>t.X==0 && t.Z==7) && !merchantMap.Any(t=>t.X==0 && t.Z==7),"Merchant hole missing");
            Require(merchantMap.Length==baseline.Length-1,"Merchant changed unrelated arena tiles");
            merchantMap[0]=new PlatformTile(23,23,TileKind.Fishbone);
            Require(baseline.SequenceEqual(BossArena.InitialTiles()),"Caller mutated shared arena template");
            Require(!BossArena.InitialTiles(config).Any(t=>t.X==23 && t.Z==23),"Caller mutated configured arena template");
        });
        yield return ("platform_validation_still_rejects_missing_duplicate_and_unpaid_tiles",()=>{
            var state=Model(1).Capture();var codec=new HookGame();RoundTrip(state);
            var missing=state.Tiles.Where(t=>t.X!=-1 || t.Z!=-1).ToArray();
            Reject(()=>codec.Decode(codec.Encode(Replace(state,tiles:missing))));
            var duplicate=state.Tiles.ToArray();duplicate[1]=duplicate[0];
            Reject(()=>codec.Decode(codec.Encode(Replace(state,tiles:duplicate))));
            var unpaid=state.Tiles.Concat(new[]{new PlatformTile(2,0,TileKind.Wood)});
            Reject(()=>codec.Decode(codec.Encode(Replace(state,tiles:unpaid))));
        });
    }
}
