using System;
using UnityEngine;

namespace Advanture.TacticalGame.NetworkView
{
    // Blender exports separate close-up and world meshes; both share one packed PBR material.
    internal sealed class Ak47Model
    {
        internal Transform Root { get; }
        internal Transform Muzzle { get; }
        internal Transform SupportWrist => support.Node;
        internal Transform FiringWrist { get; }
        private readonly Part magazine, support, carry;
        private readonly Vector3 magazineRest, wristRest, wristOnMagazine;
        private bool carrying;

        internal Ak47Model(Transform parent, bool firstPerson)
        {
            const string folder = "Weapons/AK47/";
            var prefab = Resources.Load<GameObject>(folder + (firstPerson ? "AK47_View" : "AK47_World"));
            var surface = Resources.Load<Material>(folder + "AK47_Surface");
            if (prefab == null || surface == null)
                throw new InvalidOperationException("AK47 模型或共享材质缺失。");

            Root = UnityEngine.Object.Instantiate(prefab, parent, false).transform;
            Root.name = "AK47";
            var nodes = Root.GetComponentsInChildren<Transform>(true);
            Muzzle = Find(nodes, "Muzzle");
            magazine = new Part(Find(nodes, "CurvedMagazine"), Root);
            support = new Part(Find(nodes, "SupportHand"), Root);
            carry = new Part(Find(nodes, "SupportCarry"), Root);
            carry.Node.gameObject.SetActive(false);
            FiringWrist = Find(nodes, "FiringHand");
            magazineRest = Root.InverseTransformPoint(magazine.Node.position);
            wristRest = Root.InverseTransformPoint(support.Node.position);
            wristOnMagazine = Root.InverseTransformPoint(Find(nodes, "CarrySocket").position) - magazineRest;
            foreach (var renderer in Root.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterial = surface;
                if (!firstPerson) continue;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static Transform Find(Transform[] nodes, string name)
        {
            foreach (var node in nodes)
                if (node.name == name) return node;
            throw new InvalidOperationException("AK47 缺少动作节点 " + name);
        }

        internal void PoseParts(Vector3 drop, Quaternion tilt, float reach, Quaternion passing, bool closedGrip)
        {
            magazine.Pose(drop, tilt);
            // Both glove silhouettes use the same wrist pivot. The closed pose was sculpted
            // around this socket; rotating a lowered magazine must also rotate that offset.
            Vector3 held = magazineRest + drop + tilt * wristOnMagazine;
            Vector3 wrist = (held - wristRest) * reach;
            Quaternion turn = Quaternion.Slerp(Quaternion.identity, tilt, reach) * passing;
            support.Pose(wrist, turn);
            carry.Pose(wrist, turn);
            if (closedGrip == carrying) return;
            carrying = closedGrip;
            support.Node.gameObject.SetActive(!carrying);
            carry.Node.gameObject.SetActive(carrying);
        }

        // Offsets are authored in rifle space. FBX node axes/units may differ from their parent's.
        private readonly struct Part
        {
            internal readonly Transform Node;
            private readonly Vector3 bindPosition;
            private readonly Quaternion bindRotation, rootToParent;
            private readonly Matrix4x4 rootToParentVector;

            internal Part(Transform node, Transform root)
            {
                Node = node;
                bindPosition = node.localPosition;
                bindRotation = node.localRotation;
                rootToParent = Quaternion.Inverse(node.parent.rotation) * root.rotation;
                rootToParentVector = node.parent.worldToLocalMatrix * root.localToWorldMatrix;
            }

            internal void Pose(Vector3 offset, Quaternion rotation)
            {
                Node.localPosition = bindPosition + rootToParentVector.MultiplyVector(offset);
                Node.localRotation = rootToParent * rotation * Quaternion.Inverse(rootToParent) * bindRotation;
            }
        }
    }
}
