using Advanture.Tactical.Simulation;
using UnityEngine;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>远端角色的纯显示对象，胶囊身体、头部和方向标记让移动和转向容易辨认。没有物理组件。</summary>
    public sealed class NetworkAvatar : MonoBehaviour
    {
        private static readonly string[] Names = { "Player_A_Blue", "Player_B_Orange", "Player_C_Green", "Player_D_Purple" };
        private static readonly Color[] Colors = {
            new Color(.12f,.63f,.76f), new Color(.95f,.44f,.19f), new Color(.32f,.72f,.33f), new Color(.69f,.42f,.86f)
        };
        private Transform head, leftUpper, leftLower, rightUpper, rightLower;
        private Material material, dark, sleeves;
        private bool placed, wasAlive=true;
        public Ak47View Weapon { get; private set; }
        public static NetworkAvatar Create(ushort slot, Camera observer = null)
        {
            var root = new GameObject(Names[slot]);
            var view = root.AddComponent<NetworkAvatar>();
            view.material = new Material(Shader.Find("Standard")) { color = Colors[slot] };
            view.dark = new Material(Shader.Find("Standard")) { color = new Color(.12f,.18f,.23f) };
            view.Part(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0,.78f,0), new Vector3(.6f,.65f,.6f), view.material);
            view.head = new GameObject("HeadPivot").transform; view.head.SetParent(root.transform, false); view.head.localPosition = new Vector3(0,1.58f,0);
            view.Part(PrimitiveType.Cube, "Helmet", view.head, Vector3.zero, new Vector3(.44f,.36f,.44f), view.material);
            view.Part(PrimitiveType.Cube, "Visor", view.head, new Vector3(0,.02f,.235f), new Vector3(.34f,.12f,.05f), view.dark);
            view.sleeves = new Material(Shader.Find("Standard")) { color = new Color(.29f, .305f, .245f) };
            view.sleeves.SetFloat("_Glossiness", .19f);
            view.leftUpper = view.Limb("LeftUpperSleeve", view.sleeves);
            view.leftLower = view.Limb("LeftLowerSleeve", view.sleeves);
            view.rightUpper = view.Limb("RightUpperSleeve", view.sleeves);
            view.rightLower = view.Limb("RightLowerSleeve", view.sleeves);
            var label = new GameObject("Label").AddComponent<TextMesh>(); label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0,2.15f,0); label.text = ((char)('A' + slot)).ToString(); label.fontSize = 64; label.characterSize = .08f; label.anchor = TextAnchor.MiddleCenter;
            view.Weapon=Ak47View.Create(view.head,false,observer);
            return view;
        }
        private Transform Limb(string name, Material cloth) =>
            Part(PrimitiveType.Capsule, name, transform, Vector3.zero, Vector3.one, cloth);

        private Transform Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material color)
        {
            var part = GameObject.CreatePrimitive(type); part.name = name; part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localScale = scale; part.GetComponent<Renderer>().sharedMaterial = color;
            // 显示模型绝不参与世界计算，也不能用 Unity 碰撞反过来修正网络位置。
            var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            return part.transform;
        }
        public void Present(TacticalPlayer player, Camera camera, float delta, ulong confirmedTick, float feetHeight = 0)
        {
            var target = new Vector3(player.X / 1000f, feetHeight, player.Z / 1000f);
            if(!wasAlive && player.Combat.IsAlive)placed=false;
            wasAlive=player.Combat.IsAlive;
            float blend = placed ? 1 - Mathf.Exp(-22 * delta) : 1;
            transform.position = Vector3.Lerp(transform.position, target, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, player.Yaw / 100f, player.Combat.IsAlive?0:85), blend);
            head.localRotation = Quaternion.Euler(player.Pitch / 100f, 0, 0);
            transform.Find("Label").rotation = camera.transform.rotation;
            Weapon.SampleMovement(transform, player.IsAirborne, delta);
            Weapon.Present(player.Combat,confirmedTick,delta);
            PoseArm(-1, Weapon.SupportWrist, leftUpper, leftLower);
            PoseArm(1, Weapon.FiringWrist, rightUpper, rightLower);
            placed = true;
        }
        private void PoseArm(int side, Transform wrist, Transform upper, Transform lower)
        {
            Vector3 shoulder = transform.TransformPoint(new Vector3(side * .245f, 1.34f, .026f));
            Vector3 hand = transform.InverseTransformPoint(wrist.position);
            // The supporting hand crosses the chest. Keep the elbow lateral to the torso
            // and lead it forward so the lower sleeve meets the wrist on the visible side.
            var elbowOffset = new Vector3(side * .315f, Mathf.Lerp(1.34f, hand.y, .52f) - .14f,
                Mathf.Lerp(.026f, hand.z, .57f) + .055f);
            Vector3 elbow = transform.TransformPoint(elbowOffset);
            AlignLimb(upper, shoulder, elbow, .148f);
            AlignLimb(lower, elbow, wrist.position, .112f);
        }

        private static void AlignLimb(Transform limb, Vector3 start, Vector3 end, float width)
        {
            Vector3 span = end - start;
            limb.position = (start + end) * .5f;
            limb.rotation = Quaternion.FromToRotation(Vector3.up, span);
            limb.localScale = new Vector3(width, (span.magnitude + .045f) * .5f, width);
        }

        private void OnDestroy() { if (material != null) Destroy(material); if (dark != null) Destroy(dark); if (sleeves != null) Destroy(sleeves); }
    }
}
