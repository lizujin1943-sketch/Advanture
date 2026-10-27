using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class FisherAnimationTests
    {
        private GameObject model;
        private FisherAnimationView view;
        [SetUp] public void SetUp()
        {
            var definition=Resources.Load<FisherCharacterDefinition>(FisherCharacterDefinition.ResourcePath);
            Assert.That(definition!=null && definition.Complete,Is.True,"The shipped art must include every clip.");
            model=Object.Instantiate(definition.model);
            view=new FisherAnimationView(model.GetComponent<Animator>(),definition,8);
        }
        [TearDown] public void TearDown() {view?.Dispose();if(model!=null)Object.DestroyImmediate(model);}
        private Transform Bone(string name)=>model.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);

        [Test] public void BakedCastMovesTipAndKeepsGripFeetAndWorldRootAttached()
        {
            model.transform.SetPositionAndRotation(new Vector3(3,.46f,5),Quaternion.Euler(0,37,0));
            var start=model.transform.position;var rotation=model.transform.rotation;
            var tip=view.LineGuide.position;var left=Bone("Foot_L").position;var right=Bone("Foot_R").position;
            view.Present(0,9,false,false,false);
            view.Present(.1f,9,false,false,false);
            Assert.That(Vector3.Distance(view.LineGuide.position,tip),Is.GreaterThan(.5f),"Backswing must be visible at one-metre scale.");
            Assert.That(view.LineGuide.position.y,Is.GreaterThan(start.y+1));
            for(int i=0;i<7;i++)
            {
                Assert.That(Vector3.Distance(Bone("Rod").position,Bone("Hand_R").position),Is.LessThan(.005f),"Grip must not detach during the swing.");
                Assert.That(Vector3.Distance(Bone("Foot_L").position,left),Is.LessThan(.005f));
                Assert.That(Vector3.Distance(Bone("Foot_R").position,right),Is.LessThan(.005f));
                view.Present(.1f,9,false,false,false);
            }
            Assert.That(view.IsCasting,Is.False);
            Assert.That(Vector3.Distance(view.LineGuide.position,tip),Is.LessThan(.02f));
            Assert.That(Vector3.Distance(model.transform.position,start),Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(model.transform.rotation,rotation),Is.LessThan(.001f));
        }

        [Test] public void HistoryAndAcknowledgementDoNotReplayButRejectedPredictionCanRetry()
        {
            view.Present(.1f,8,false,false,false);
            Assert.That(view.PresentedCasts,Is.Zero);
            view.Present(0,9,false,false,false);
            for(int i=0;i<8;i++)view.Present(.1f,9,false,false,false);
            Assert.That(view.PresentedCasts,Is.EqualTo(1));
            Assert.That(view.IsCasting,Is.False);
            view.Present(0,10,false,false,false);
            view.Present(.1f,9,false,false,false);
            Assert.That(view.IsCasting,Is.False,"Rollback must discard the predicted gesture.");
            view.Present(0,10,false,false,false);
            Assert.That(view.IsCasting,Is.True);
            Assert.That(view.PresentedCasts,Is.EqualTo(3));
        }

        [Test] public void SubframeSamplingDoesNotFlipTheRodThroughTheBody()
        {
            view.Present(0,9,false,false,false);var previous=view.LineGuide.position;
            for(int i=0;i<60;i++)
            {
                view.Present(.01f,9,false,false,false);
                Assert.That(Vector3.Distance(view.LineGuide.position,previous),Is.LessThan(.45f),
                    "A fast swing must still interpolate continuously between baked frames: sample "+i);
                Assert.That(view.LineGuide.position.y,Is.GreaterThan(0f),"The cast must not flip through the floor.");
                previous=view.LineGuide.position;
            }
        }

        [Test] public void InterruptionReturnsToIdleAndRecoveryDoesNotReplayAnOldGesture()
        {
            var tip=view.LineGuide.position;
            view.Present(0,9,false,false,false);view.Present(.1f,9,false,false,false);
            view.Present(.016f,9,false,false,true);
            Assert.That(view.IsCasting,Is.False);
            Assert.That(Vector3.Distance(tip,view.LineGuide.position),Is.LessThan(.005f));
            view.Present(.1f,9,false,false,false);
            Assert.That(view.PresentedCasts,Is.EqualTo(1));
            view.Present(0,10,false,false,false);
            Assert.That(view.IsCasting,Is.True);
        }

        [Test] public void ForearmsAndHandsStayOutsideBodyAcrossCastAndLocomotionBlends()
        {
            var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.name=="Fisher Body");
            var vertices=skin.sharedMesh.vertices;
            var weights=skin.sharedMesh.boneWeights;
            var bindposes=skin.sharedMesh.bindposes;
            var bones=skin.bones;
            var regions=new[]{"Spine","Head"}.Select(name=>System.Array.FindIndex(bones,b=>b.name==name)).ToArray();
            var cores=new Bounds[regions.Length];
            var surfaces=new Vector3[regions.Length][];
            var triangles=skin.sharedMesh.triangles;
            for(int region=0;region<regions.Length;region++)
            {
                var bodyVertices=Enumerable.Range(0,vertices.Length)
                    .Where(i=>weights[i].boneIndex0==regions[region] && weights[i].weight0>.99f).ToArray();
                Assert.That(bodyVertices.Length,Is.GreaterThan(0));
                var bounds=new Bounds(vertices[bodyVertices[0]],Vector3.zero);
                foreach(int i in bodyVertices)bounds.Encapsulate(vertices[i]);
                // Exclude bevels, clothing details and the shoulder attachment seam.
                // This guards actual skinned geometry inside the body's solid core,
                // not merely a continuous rod tip or an attached hand socket.
                bounds.size*=region==0?.85f:.75f;
                cores[region]=bounds;
                surfaces[region]=Enumerable.Range(0,triangles.Length/3)
                    .Where(t=>Enumerable.Range(0,3).All(c=>weights[triangles[t*3+c]].boneIndex0==regions[region]))
                    .SelectMany(t=>new[]{vertices[triangles[t*3]],vertices[triangles[t*3+1]],vertices[triangles[t*3+2]]}).ToArray();
                Assert.That(InsideClosedSurfaces(bounds.center,surfaces[region]),Is.True,
                    "The body centre must be inside its authored surface; malformed or open geometry invalidates this check.");
            }
            var armVertices=Enumerable.Range(0,vertices.Length).Where(i=>
                bones[weights[i].boneIndex0].name.StartsWith("Forearm_") ||
                bones[weights[i].boneIndex0].name.StartsWith("Hand_")).ToArray();
            Assert.That(armVertices.Length,Is.GreaterThan(0));
            for(int sample=0;sample<400;sample++)
            {
                // Idle, cast, reel, walk, then blend back to idle at 10 ms intervals.
                view.Present(.01f,sample>=20?9:8,sample>=210 && sample<310,sample>=90 && sample<210,false);
                var deformations=bones.Select((bone,i)=>bone.localToWorldMatrix*bindposes[i]).ToArray();
                for(int region=0;region<regions.Length;region++)
                {
                    var inverse=deformations[regions[region]].inverse;
                    foreach(int index in armVertices)
                    {
                        int bone=weights[index].boneIndex0;
                        var point=inverse.MultiplyPoint3x4(deformations[bone].MultiplyPoint3x4(vertices[index]));
                        // The rounded jacket leaves empty space inside its box corners.
                        // Confirm penetration against the actual closed surfaces before failing.
                        if(cores[region].Contains(point) && InsideClosedSurfaces(point,surfaces[region]))
                            Assert.Fail(bones[bone].name+" clips inside "+bones[regions[region]].name+
                                " at sample "+sample+", vertex "+index);
                    }
                }
            }
        }

        private static bool InsideClosedSurfaces(Vector3 point,Vector3[] triangles)
        {
            // Oriented solid angles are 4*pi inside a closed solid and zero outside.
            // Summing supports overlapping jacket/bib pieces without the parity
            // cancellation of a single ray crossing overlapping closed meshes.
            double angle=0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=triangles[i]-point;var b=triangles[i+1]-point;var c=triangles[i+2]-point;
                double denominator=a.magnitude*b.magnitude*c.magnitude+
                    Vector3.Dot(a,b)*c.magnitude+Vector3.Dot(b,c)*a.magnitude+Vector3.Dot(c,a)*b.magnitude;
                angle+=2*System.Math.Atan2(Vector3.Dot(a,Vector3.Cross(b,c)),denominator);
            }
            return System.Math.Abs(angle)>2*System.Math.PI;
        }
    }
}
