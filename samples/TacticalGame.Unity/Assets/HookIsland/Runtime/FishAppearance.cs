using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Species silhouettes and grade accents. Cosmetic sizing never changes simulation state.</summary>
    internal static class FishAppearance
    {
        private static readonly Vector3[] Bodies={new Vector3(.42f,.32f,.74f),new Vector3(.3f,.3f,.9f),
            new Vector3(.43f,.45f,.66f),new Vector3(.52f,.4f,.8f),new Vector3(.6f,.58f,.6f),
            new Vector3(.4f,.48f,.64f),new Vector3(.2f,.6f,.68f),new Vector3(.42f,.34f,.62f),
            new Vector3(.28f,.3f,1.1f),new Vector3(.2f,.27f,1.45f)};
        internal static void Apply(Transform fish,int species,int grade,Material color,Material accent)
        {
            var body=fish.Find("Body");body.localScale=Bodies[species];body.GetComponent<MeshRenderer>().sharedMaterial=color;
            var tail=fish.Find("Tail");tail.localPosition=new Vector3(0,0,-Bodies[species].z*.83f);
            tail.GetComponent<MeshRenderer>().sharedMaterial=color;
            var fin=fish.Find("Top fin");fin.localScale=new Vector3(.06f,species==7 || species==8?.62f:.27f,.3f);
            fin.GetComponent<MeshRenderer>().sharedMaterial=grade>1?accent:color;
            fish.Find("Belly").localScale=new Vector3(Bodies[species].x*.78f,Bodies[species].y*.58f,Bodies[species].z*.65f);
        }
    }
}
