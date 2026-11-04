using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    /// <summary>直接检查导入 FBX 的眼睛与蟹壳位置，避免只验证逻辑根节点而漏掉模型倒向。</summary>
    public sealed class CrabPresentationTests
    {
        [TestCase(1000000,0)]
        [TestCase(-1000000,0)]
        [TestCase(0,1000000)]
        [TestCase(0,-1000000)]
        public void ImportedFaceAndPinchWarningFollowAuthoritativeDirection(int directionX,int directionZ)
        {
            var parent=new GameObject("Crab facing acceptance");var config=new CrabConfig();
            var view=new CrabPresentation(parent.transform,Shader.Find("Sprites/Default"),config);
            try
            {
                foreach(var phase in new[]{CrabPhase.Chasing,CrabPhase.Windup,CrabPhase.Pinching,CrabPhase.Recovering})
                {
                    var c=new CrabState{Generation=1,Phase=phase,Health=100,Timer=3,DirectionX=directionX,DirectionZ=directionZ};
                    view.Synchronize(new HookState(100,Array.Empty<Fisher>(),Array.Empty<Fish>(),0,crabs:new[]{c}));
                    var joints=parent.GetComponentsInChildren<Transform>(true);
                    var eyes=joints.Where(t=>t.name=="Eye" || t.name.StartsWith("Eye.",StringComparison.Ordinal)).ToArray();
                    Assert.That(eyes.Length,Is.EqualTo(2),"Actual FBX eye markers are missing");
                    Vector3 face=eyes.Aggregate(Vector3.zero,(sum,t)=>sum+t.position)/eyes.Length-joints.Single(t=>t.name=="Carapace").position;
                    Assert.That(face.y,Is.GreaterThan(.1f),"Facing correction flipped the imported crab upside down");
                    face.y=0;var expected=new Vector3(directionX,0,directionZ).normalized;
                    Assert.That(Vector3.Dot(face.normalized,expected),Is.GreaterThan(.999f),"The actual crab model faces away during "+phase);
                    var warning=joints.Single(t=>t.name=="Pinch warning").GetComponent<LineRenderer>();
                    Assert.That(Vector3.Dot(warning.transform.forward,expected),Is.GreaterThan(.999f),"Model correction rotated the authoritative warning");
                    if(phase==CrabPhase.Windup)
                    {
                        Vector3 middle=warning.transform.TransformPoint(warning.GetPosition(9))-warning.transform.position;middle.y=0;
                        Assert.That(Vector3.Dot(face.normalized,middle.normalized),Is.GreaterThan(.999f),"Claws and pinch sector disagree");
                    }
                }
            }
            finally{view.Dispose();Object.DestroyImmediate(parent);}
        }
    }
}
