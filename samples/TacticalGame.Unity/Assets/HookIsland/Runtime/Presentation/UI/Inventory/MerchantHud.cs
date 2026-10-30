using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Merchant display names for shop pages and scene objects.</summary>
    public static class MerchantHud
    {
        public static string Name(FishingConfig config,int id)
        {
            if(id<=0 || !MerchantRules.Exists(config,id))return "平台商店";
            var p=MerchantRules.Position(config,id);
            string side=Mathf.Abs(p.X)>=Mathf.Abs(p.Z)?(p.X>=0?"东":"西"):(p.Z>=0?"北":"南");
            return side+"侧补给船";
        }
    }
}
