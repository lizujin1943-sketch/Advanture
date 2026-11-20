using Advanture.Tactical.Network;
using Advanture.Tactical.Simulation;
using UnityEngine;

namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>武器只消费确认的战斗事件，后坐和手部动作只作用于显示网格。</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class Ak47View : MonoBehaviour
    {
        private const int ViewLayer = 31;
        private const float ReloadTicks = (CombatState.ReloadDurationMilliseconds * TacticalClient.TickRate + 999) / 1000;
        private Ak47Model model;
        private Ak47Motion motion;
        private Ak47ShotEffects effects;
        private Ak47Audio sound;
        private Camera worldCamera, weaponCamera;
        private bool ownsMask, originalLayerVisible, haveSnapshot, alive = true;
        private ulong lastShot, newestTick, reloadEnd;
        private float subTick;
        private int nextCue;

        public int PresentedShots { get; private set; }
        public bool IsVisible => model != null && model.Root.gameObject.activeSelf;
        internal Transform SupportWrist => model.SupportWrist;
        internal Transform FiringWrist => model.FiringWrist;

        public static Ak47View Create(Transform parent, bool firstPerson, Camera observer = null)
        {
            var obj = new GameObject(firstPerson ? "LocalAK47" : "RemoteAK47");
            obj.transform.SetParent(parent, false);
            var view = obj.AddComponent<Ak47View>();
            view.Initialize(firstPerson, observer);
            return view;
        }

        private void Initialize(bool firstPerson, Camera observer)
        {
            transform.localPosition = firstPerson ? new Vector3(.224f, -.180f, .535f) : new Vector3(.198f, -.241f, .30f);
            transform.localRotation = firstPerson ? Quaternion.Euler(-3.5f, -12, -3) : Quaternion.identity;
            model = new Ak47Model(transform, firstPerson);
            motion = new Ak47Motion(model, firstPerson);
            worldCamera = firstPerson ? transform.parent.GetComponent<Camera>() : observer;
            if (firstPerson && worldCamera != null)
            {
                foreach (var node in model.Root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = ViewLayer;
                var overlay = new GameObject("WeaponCamera");
                overlay.transform.SetParent(worldCamera.transform, false);
                weaponCamera = overlay.AddComponent<Camera>();
                weaponCamera.clearFlags = CameraClearFlags.Depth;
                weaponCamera.cullingMask = 1 << ViewLayer;
                weaponCamera.depth = worldCamera.depth + 1;
                weaponCamera.rect = worldCamera.rect;
                weaponCamera.fieldOfView = 59;
                weaponCamera.nearClipPlane = .018f;
                weaponCamera.farClipPlane = 4;
                weaponCamera.allowHDR = worldCamera.allowHDR;
                weaponCamera.allowMSAA = worldCamera.allowMSAA;
                EnableOverlay();
            }
            sound = new Ak47Audio(gameObject, firstPerson);
            effects = new Ak47ShotEffects(transform, model.Muzzle, firstPerson, worldCamera, weaponCamera);
        }

        internal void SampleMovement(Transform viewpoint, bool airborne, float delta) =>
            motion.SampleMovement(viewpoint, airborne, delta);

        public void Present(CombatState state, ulong confirmedTick, float delta)
        {
            delta = Mathf.Max(0, delta);
            effects.Tick(delta);
            motion.Advance(delta);
            // A previously displayed snapshot cannot rewind a sound, reload gesture or death.
            if (haveSnapshot && confirmedTick < newestTick) return;
            bool firstSnapshot = !haveSnapshot;
            subTick = haveSnapshot && confirmedTick == newestTick ? Mathf.Min(1, subTick + delta * TacticalClient.TickRate) : 0;
            newestTick = confirmedTick;
            haveSnapshot = true;

            if (alive != state.IsAlive)
            {
                alive = state.IsAlive;
                model.Root.gameObject.SetActive(alive);
                effects.Reset();
                sound.Stop();
                motion.Reset();
                reloadEnd = 0;
            }

            bool freshShot = false;
            if (state.ShotSequence > lastShot)
            {
                lastShot = state.ShotSequence;
                freshShot = confirmedTick >= state.LastShotTick && confirmedTick - state.LastShotTick <= 3 &&
                    (alive || confirmedTick == state.LastShotTick);
            }
            if (alive)
            {
                float reload = ReloadProgress(state.ReloadUntilTick, confirmedTick, firstSnapshot);
                if (freshShot) motion.Shoot(lastShot);
                motion.Pose(reload);
            }
            if (freshShot)
            {
                PresentedShots++;
                sound.PlayShot(lastShot);
                effects.Fire(state, lastShot);
            }
        }

        private float ReloadProgress(ulong end, ulong tick, bool firstSnapshot)
        {
            if (end <= tick)
            {
                reloadEnd = 0;
                return 0;
            }
            float progress = Mathf.Clamp01(1 - ((float)(end - tick) - subTick) / ReloadTicks);
            bool newAction = reloadEnd != end;
            if (newAction) { reloadEnd = end; nextCue = 0; }
            // If a new view joins halfway through, consume old cue edges silently. An ordinary
            // dropped frame may cross multiple edges; play only the most recent one still in time.
            int latest = -1;
            while (nextCue < 3 && progress >= CueTime(nextCue)) latest = nextCue++;
            if (latest >= 0 && !firstSnapshot && progress - CueTime(latest) < .075f)
                sound.PlayReload(latest);
            return progress;
        }

        private static float CueTime(int cue) => cue == 0 ? .25f : cue == 1 ? .68f : .88f;

        private void EnableOverlay()
        {
            if (weaponCamera == null || ownsMask) return;
            originalLayerVisible = (worldCamera.cullingMask & (1 << ViewLayer)) != 0;
            worldCamera.cullingMask &= ~(1 << ViewLayer);
            weaponCamera.enabled = true;
            ownsMask = true;
        }

        private void OnEnable() => EnableOverlay();

        private void OnDisable()
        {
            if (ownsMask)
            {
                if (worldCamera != null && originalLayerVisible) worldCamera.cullingMask |= 1 << ViewLayer;
                if (weaponCamera != null) weaponCamera.enabled = false;
                ownsMask = false;
            }
            effects?.Reset();
            sound?.Stop();
        }

        private void OnDestroy()
        {
            effects?.Dispose();
            if (weaponCamera == null) return;
            if (Application.isPlaying) Destroy(weaponCamera.gameObject);
            else DestroyImmediate(weaponCamera.gameObject);
        }
    }
}
