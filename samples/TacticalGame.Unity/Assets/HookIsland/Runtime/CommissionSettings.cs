using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [Serializable] public sealed class CommissionObjective
    {
        [Range(1,1000)] public int fish=6,rare=2,bait=1;
    }
    [CreateAssetMenu(menuName="Hook Island/Commission Settings")]
    public sealed class CommissionSettings:ScriptableObject
    {
        public bool useCommissions=true;
        public CommissionObjective[] objectives={new CommissionObjective(),new CommissionObjective {fish=10,rare=3},new CommissionObjective {fish=14,rare=4}};
        public CommissionConfig CreateConfig()=>useCommissions?new CommissionConfig(objectives.Select(d=>new CommissionDefinition(d.fish,d.rare,d.bait))):CommissionConfig.Disabled;
    }
}
