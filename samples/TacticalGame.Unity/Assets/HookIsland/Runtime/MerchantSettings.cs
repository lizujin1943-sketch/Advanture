using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [Serializable] public sealed class MerchantLocation
    {
        [Range(1,5)] public int level;
        public int cellX,cellZ;
        public MerchantLocation(int level,int x,int z){this.level=level;cellX=x;cellZ=z;}
    }
    [CreateAssetMenu(menuName="Hook Island/Merchant Settings")]
    public sealed class MerchantSettings:ScriptableObject
    {
        public bool useMerchants=true;
        public MerchantLocation[] ships={new MerchantLocation(2,5,0),new MerchantLocation(3,0,7),new MerchantLocation(4,-9,0),new MerchantLocation(5,0,-11)};
        public MerchantConfig CreateConfig()
        {
            if(!useMerchants)return MerchantConfig.Empty;
            if(ships==null)throw new InvalidOperationException("Merchant ships configuration is missing.");
            var definitions=new MerchantDefinition[ships.Length];
            for(int n=0;n<ships.Length;n++)
            {
                var ship=ships[n]??throw new InvalidOperationException("Merchant ship configuration is missing.");
                definitions[n]=new MerchantDefinition(n+1,ship.level,ship.cellX,ship.cellZ);
            }
            return new MerchantConfig(definitions);
        }
    }
}
