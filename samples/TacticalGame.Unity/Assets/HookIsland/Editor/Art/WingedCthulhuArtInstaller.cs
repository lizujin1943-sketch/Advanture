using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Editor
{
    /// <summary>替换 Boss 的美术资源；保留确认帧采样、逐腕叠加和权威结算。</summary>
    public static class WingedCthulhuArtInstaller
    {
        public const string ArtPath = "Assets/HookIsland/Art/WingedCthulhu";
        public const string PrefabPath = ArtPath + "/WingedCthulhu.prefab";
        public const string ClipsPath = ArtPath + "/Animations";
        private const string ModelPath = ArtPath + "/WingedCthulhu.fbx";
        [Serializable] private sealed class Manifest
        {
            public int schema, fps, meshCount, boneCount, meshTriangles;
            public float controlRootWidth;
            public string forwardSocket, centerSocket;
            public Clip[] clips; public Part[] meshes;
        }
        [Serializable] private sealed class Clip
        {
            public string name, kind, limb; public int frameCount;
            public float durationSeconds; public bool loop; public string[] bones;
        }
        [Serializable] private sealed class Part { public string name; public string[] materials; }
        [Serializable] private sealed class CollisionManifest { public Limb[] limbs; public CollisionClip[] clips; }
        [Serializable] private sealed class Limb { public string id; public int pointOffset, pointCount; }
        [Serializable] private sealed class CollisionClip { public string name; public int offsetBytes; }
        [Serializable] private sealed class Report
        {
            public string status = "PASS";
            public int clips, bones, meshes, triangles;
            public float scaleCorrection, maxIdleJointErrorMeters;
        }

        [MenuItem("Tools/Hook Island/Install Winged Cthulhu Boss Art")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先停止运行再导入 Boss 美术。");
            if (Path.GetFullPath(Application.dataPath) != Path.GetFullPath(Path.Combine(Repository(), "samples/TacticalGame.Unity/Assets")))
                throw new InvalidOperationException("只能在 TacticalGame.Unity 工程导入。");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ArtPath + "/unity-manifest.json"));
            if (manifest.schema != 1 || manifest.fps != 30 || manifest.clips.Length != 24)
                throw new InvalidDataException("带翼克苏鲁动作清单不完整。");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Folder(ClipsPath); Folder(ArtPath + "/Materials");
            ConfigureTextures();
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("缺少带翼克苏鲁 FBX", ModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true; importer.importBlendShapes = false;
            importer.importCameras = importer.importLights = importer.addCollider = false;
            importer.globalScale = 1; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.SaveAndReimport();
            var defaults = importer.defaultClipAnimations;
            foreach (var clip in defaults)
            {
                clip.name = clip.name.Split('|').Last();
                var source = manifest.clips.Single(c => c.name == clip.name);
                clip.loopTime = source.loop; clip.loopPose = false;
                clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            if (defaults.Length != 24) throw new InvalidDataException("FBX 必须包含 24 个动作。");
            importer.clipAnimations = defaults; importer.SaveAndReimport();
            var root = new GameObject("WingedCthulhu");
            try
            {
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), root.transform);
                model.name = "Model";
                var transforms = model.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                var imported = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToDictionary(c => c.name);
                var clips = manifest.clips.Select(s => Extract(imported[s.name], s)).ToArray();
                clips.Single(c => c.name == "AW_Idle").SampleAnimation(model, 0);
                float width = Vector3.ProjectOnPlane(transforms["CTRL_A04"].position - transforms["CTRL_A01"].position, Vector3.up).magnitude;
                float correction = manifest.controlRootWidth / width;
                if (float.IsNaN(correction) || correction < .001f || correction > 1000) throw new InvalidDataException("FBX 尺度异常。");
                model.transform.localScale *= correction;
                var forward = Vector3.ProjectOnPlane(transforms[manifest.forwardSocket].position - transforms[manifest.centerSocket].position, Vector3.up);
                model.transform.rotation = Quaternion.FromToRotation(forward.normalized, Vector3.back) * model.transform.rotation;
                model.transform.position -= transforms["ROOT"].position;
                if (Vector3.Dot((transforms["CTRL_A04"].position - transforms["CTRL_A01"].position).normalized, Vector3.right) < .999f)
                    throw new InvalidDataException("FBX 左右方向与权威采样不一致。");
                var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (skins.Length != manifest.meshCount) throw new InvalidDataException("蒙皮网格数量不一致。");
                foreach (var skin in skins)
                {
                    skin.sharedMaterials = manifest.meshes.Single(m => m.name == skin.name).materials.Select(Material).ToArray();
                    skin.updateWhenOffscreen = true; skin.quality = SkinQuality.Bone4;
                    var bounds = new Bounds(skin.transform.InverseTransformPoint(new Vector3(0, -1, 0)), Vector3.zero);
                    for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                        bounds.Encapsulate(skin.transform.InverseTransformPoint(new Vector3(x * 27, y * 20 - 1, z * 27)));
                    skin.localBounds = bounds;
                    if (skin.name.StartsWith("AW_Stump_", StringComparison.Ordinal)) skin.gameObject.SetActive(false);
                }
                var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.enabled = false;
                float error = ValidateIdle(transforms);
                if (error > .008f) throw new InvalidDataException("新模型骨骼与权威采样不一致：" + error);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var settings = AssetDatabase.LoadAssetAtPath<BossSettings>(BossChallengeInstaller.SettingsPath);
                if (settings == null) throw new InvalidDataException("现有 BossSettings 缺失，禁止新建默认玩法配置。");
                settings.modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); settings.clips = clips;
                EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
                File.WriteAllText(ArtPath + "/unity-import-report.json", JsonUtility.ToJson(new Report
                { clips = clips.Length, bones = manifest.boneCount, meshes = skins.Length, triangles = manifest.meshTriangles,
                    scaleCorrection = correction, maxIdleJointErrorMeters = error }, true));
                AssetDatabase.Refresh(); Debug.Log("WINGED_CTHULHU_INSTALLED: model, textures, 24 clips and BossSettings replaced.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static AnimationClip Extract(AnimationClip source, Clip spec)
        {
            string path = ClipsPath + "/" + spec.name + ".anim";
            var result = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (result == null) { result = new AnimationClip(); AssetDatabase.CreateAsset(result, path); }
            EditorUtility.CopySerialized(source, result); result.name = spec.name; result.frameRate = 30;
            var owned = new HashSet<string>(spec.bones); var bindings = AnimationUtility.GetCurveBindings(result);
            var curves = bindings.Select(b => b.type == typeof(Transform) && owned.Contains(b.path.Split('/').Last())
                // 0.05 mm position / 5e-5 quaternion component tolerance. Validate the
                // resulting world joints against the authoritative samples below and in tests.
                ? AbyssalWatcherArtInstaller.ReduceCurve(AnimationUtility.GetEditorCurve(result, b), spec.durationSeconds, .00005f) : null).ToArray();
            AnimationUtility.SetEditorCurves(result, bindings, curves);
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(result)) AnimationUtility.SetObjectReferenceCurve(result, binding, null);
            AnimationUtility.SetAnimationEvents(result, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(result); settings.loopTime = spec.loop; settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(result, settings); EditorUtility.SetDirty(result); return result;
        }
        private static Material Material(string name)
        {
            string path = ArtPath + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Hook Island/Winged Cthulhu");
            if (shader == null) throw new InvalidDataException("缺少带翼克苏鲁着色器。");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader) { name = name }; AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader; mat.color = Color.white;
            string group = name.Substring(3); bool eyes = group == "Eyes", stump = group == "Stump";
            mat.SetTexture("_MainTex", Texture(group, "BaseColor"));
            mat.SetTexture("_RoughnessMap", Texture(group, "Roughness"));
            mat.SetTexture("_NormalMap", Texture(group, "Normal"));
            mat.SetFloat("_RoughnessScale", eyes ? .3f : stump ? .43f : 1);
            mat.SetFloat("_NormalStrength", eyes || stump ? 0 : 1);
            mat.SetColor("_EmissionColor", eyes ? new Color(.72f, 1f, .08f) * 1.8f : Color.black);
            if (eyes) mat.color = new Color(.83f, .95f, .31f);
            if (stump) mat.color = new Color(.50f, .29f, .38f);
            mat.enableInstancing = true; EditorUtility.SetDirty(mat); return mat;
        }
        private static Texture2D Texture(string group, string kind) => AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "/Textures/T_WC_" + group + "_" + kind + ".png");
        private static void ConfigureTextures()
        {
            foreach (string path in Directory.GetFiles(ArtPath + "/Textures", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path); bool normal = path.Contains("_Normal");
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = path.Contains("_BaseColor"); importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4; importer.maxTextureSize = path.Contains("_Wings_") ? 2048 : 4096;
                importer.alphaSource = TextureImporterAlphaSource.None; importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }
        private static float ValidateIdle(IDictionary<string, Transform> transforms)
        {
            string source = Path.Combine(Repository(), "source-assets/creatures/winged-cthulhu/exports/unity");
            var m = JsonUtility.FromJson<CollisionManifest>(File.ReadAllText(Path.Combine(source, "collision-manifest.json")));
            int offset = m.clips.Single(c => c.name == "AW_Idle").offsetBytes; float error = 0;
            using (var stream = File.OpenRead(Path.Combine(source, "collision-joints.bin"))) using (var reader = new BinaryReader(stream))
                foreach (var limb in m.limbs) for (int i = 0; i < limb.pointCount - 1; i++)
                {
                    stream.Position = offset + (limb.pointOffset + i) * 16;
                    var expected = new Vector3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()) / 1000;
                    var actual = transforms["DEF_" + limb.id + "_" + i.ToString("00")].position;
                    error = Mathf.Max(error, Vector3.Distance(expected, actual));
                }
            return error;
        }
        private static string Repository() => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
