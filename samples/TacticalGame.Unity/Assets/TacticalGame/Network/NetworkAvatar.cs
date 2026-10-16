using Advanture.Tactical.Simulation;
using UnityEngine;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>远端角色的纯显示对象，胶囊身体、头部和方向标记让移动和转向容易辨认。没有物理组件。</summary>
    public sealed class NetworkAvatar : MonoBehaviour
    {
        private Transform head;
        private Material material, dark;
        private bool placed, wasAlive=true;
        public Ak47View Weapon { get; private set; }
        public static NetworkAvatar Create(ushort slot)
        {
            var root = new GameObject(slot == 0 ? "Player_A_Blue" : "Player_B_Orange");
            var view = root.AddComponent<NetworkAvatar>();
            view.material = new Material(Shader.Find("Standard")) { color = slot == 0 ? new Color(.12f,.63f,.76f) : new Color(.95f,.44f,.19f) };
            view.dark = new Material(Shader.Find("Standard")) { color = new Color(.12f,.18f,.23f) };
            view.Part(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0,.78f,0), new Vector3(.6f,.65f,.6f), view.material);
            view.head = new GameObject("HeadPivot").transform; view.head.SetParent(root.transform, false); view.head.localPosition = new Vector3(0,1.58f,0);
            view.Part(PrimitiveType.Cube, "Helmet", view.head, Vector3.zero, new Vector3(.44f,.36f,.44f), view.material);
            view.Part(PrimitiveType.Cube, "Visor", view.head, new Vector3(0,.02f,.235f), new Vector3(.34f,.12f,.05f), view.dark);
            view.Part(PrimitiveType.Cube, "ForwardMarker", view.head, new Vector3(0,-.11f,.42f), new Vector3(.09f,.08f,.4f), view.dark);
            var label = new GameObject("Label").AddComponent<TextMesh>(); label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0,2.15f,0); label.text = slot == 0 ? "A" : "B"; label.fontSize = 64; label.characterSize = .08f; label.anchor = TextAnchor.MiddleCenter;
            view.Weapon=Ak47View.Create(view.head,false);
            return view;
        }
        private void Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material color)
        {
            var part = GameObject.CreatePrimitive(type); part.name = name; part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localScale = scale; part.GetComponent<Renderer>().sharedMaterial = color;
            // 显示模型绝不参与世界计算，也不能用 Unity 碰撞反过来修正网络位置。
            var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        }
        public void Present(TacticalPlayer player, Camera camera, float delta, ulong confirmedTick)
        {
            var target = new Vector3(player.X / 1000f, 0, player.Z / 1000f);
            if(!wasAlive && player.Combat.IsAlive)placed=false;
            wasAlive=player.Combat.IsAlive;
            float blend = placed ? 1 - Mathf.Exp(-22 * delta) : 1;
            transform.position = Vector3.Lerp(transform.position, target, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, player.Yaw / 100f, player.Combat.IsAlive?0:85), blend);
            head.localRotation = Quaternion.Euler(player.Pitch / 100f, 0, 0);
            transform.Find("Label").rotation = camera.transform.rotation;
            Weapon.Present(player.Combat,confirmedTick,delta);
            placed = true;
        }
        private void OnDestroy() { if (material != null) Destroy(material); if (dark != null) Destroy(dark); }
    }
}
