using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Editor
{
    /// <summary>显式导入已批准的本体美术；不写地图、战斗配置或任何权威状态。</summary>
    public static class AbyssalWatcherArtInstaller
    {
        public const string ArtPath = "Assets/HookIsland/Art/AbyssalWatcher";
        public const string ModelPath = ArtPath + "/AbyssalWatcher.fbx";
        public const string PrefabPath = ArtPath + "/AbyssalWatcher.prefab";
        public const string ClipsPath = ArtPath + "/Animations";
        private const string MaterialPath = ArtPath + "/Materials";

        [Serializable] private sealed class Manifest
        {
            public int schema, fps, meshCount, boneCount;
            public Clip[] clips;
            public MeshEntry[] meshes;
        }
        [Serializable] private sealed class Clip
        {
            public string name, kind, limb;
            public int frameCount;
            public float durationSeconds;
            public bool loop, face;
            public string[] bones;
            public float[] eyeGlow, mouthOpen, blinkCenter, blinkL, blinkR;
        }
        [Serializable] private sealed class MeshEntry
        {
            public string name;
            public string[] materials;
        }
        [Serializable] private sealed class CollisionManifest
        {
            public int frameStrideBytes, jointCount;
            public CollisionLimb[] limbs;
            public CollisionClip[] clips;
        }
        [Serializable] private sealed class CollisionLimb
        {
            public string id;
            public int pointOffset, pointCount;
        }
        [Serializable] private sealed class CollisionClip
        {
            public string name;
            public int offsetBytes;
        }
        [Serializable] private sealed class ImportReport
        {
            public string status = "PASS", scope = "Unity importer, independent animation curves, canonical axes, collision pose agreement";
            public int meshCount, boneCount, clipCount;
            public float metersScaleCorrection, maxIdleJointCoordinateErrorMeters;
            public float maxBlendShapeWeightError;
            public Vector3 modelEulerCorrection;
            public string[] clipNames;
        }

        [MenuItem("Tools/Hook Island/Install Abyssal Watcher Art")]
        public static void Install() => InstallInternal(true);

        [MenuItem("Tools/Hook Island/Refresh Abyssal Watcher Animation Clips")]
        public static void RefreshAnimationClips() => InstallInternal(false);

        private static void InstallInternal(bool importModel)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先停止运行再导入深渊凝视者美术。");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ArtPath + "/unity-manifest.json"));
            if (manifest.schema != 1 || manifest.fps != 30 || manifest.clips.Length != 24)
                throw new InvalidDataException("深渊凝视者导出清单不完整。");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("请先运行 export_abyssal_unity.py 生成正式 FBX。", ModelPath);
            if (importModel)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = importer.importBlendShapes = true;
                importer.importCameras = importer.importLights = importer.addCollider = false;
                importer.globalScale = 1;
                importer.useFileScale = true;
                importer.bakeAxisConversion = true;
                importer.preserveHierarchy = true;
                importer.optimizeGameObjects = false;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
                importer.SaveAndReimport();
                var settings = importer.defaultClipAnimations;
                foreach (var clip in settings)
                {
                    clip.name = clip.name.Split('|').Last();
                    var source = manifest.clips.Single(c => c.name == clip.name);
                    clip.loopTime = source.loop;
                    clip.loopPose = false;
                    clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                    clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
                }
                if (settings.Length != manifest.clips.Length) throw new InvalidDataException("FBX 动作数量与导出清单不符。");
                importer.clipAnimations = settings;
                importer.SaveAndReimport();
            }
            EnsureFolder(ClipsPath);
            EnsureFolder(MaterialPath);
            if (importModel) ConfigureTextures();
            var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = new GameObject("AbyssalWatcher");
            try
            {
                var model = Object.Instantiate(sourceModel, root.transform);
                model.name = "Model";
                var transforms = model.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                var allClips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToDictionary(c => c.name);
                var extracted = new Dictionary<string, AnimationClip>();
                foreach (var spec in manifest.clips)
                    extracted.Add(spec.name, ExtractClip(allClips[spec.name], spec, model.transform, transforms));
                extracted["AW_Idle"].SampleAnimation(model, 0);
                // 用实际导入骨架校准 FBX 坐标约定，最终顶层容器恒为单位变换、正面 -Z。
                var width = Vector3.ProjectOnPlane(transforms["CTRL_A04"].position - transforms["CTRL_A01"].position, Vector3.up).magnitude;
                var targetWidth = 2 * 2.15f * Mathf.Sin(65 * Mathf.Deg2Rad);
                var scaleCorrection = targetWidth / width;
                if (!IsFinite(scaleCorrection) || scaleCorrection < .001f || scaleCorrection > 1000)
                    throw new InvalidDataException("FBX 单位或骨架范围异常。");
                model.transform.localScale *= scaleCorrection;
                var forward = Vector3.ProjectOnPlane(transforms["DEF_Eye_Center"].position - transforms["DEF_Head"].position, Vector3.up).normalized;
                model.transform.rotation = Quaternion.FromToRotation(forward, Vector3.back) * model.transform.rotation;
                model.transform.position -= transforms["ROOT"].position;
                var right = (transforms["CTRL_A04"].position - transforms["CTRL_A01"].position).normalized;
                if (Vector3.Dot(right, Vector3.right) < .999f)
                    throw new InvalidDataException("导入左右方向与碰撞采样不一致，不能安装镜像本体。");
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(ArtPath + "/AbyssalSurface.shader");
                if (shader == null) throw new InvalidDataException("缺少深渊凝视者表面材质。");
                var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length != manifest.meshCount) throw new InvalidDataException("导入遗漏本体蒙皮网格。");
                foreach (var renderer in renderers)
                {
                    var mesh = manifest.meshes.Single(m => m.name == renderer.name);
                    renderer.sharedMaterials = mesh.materials.Select(n => GetMaterial(n, shader)).ToArray();
                    renderer.updateWhenOffscreen = true;
                    renderer.quality = SkinQuality.Bone4;
                    renderer.localBounds = LocalBounds(renderer.transform, new Bounds(new Vector3(0, -1, 0), new Vector3(52, 36, 52)));
                }
                var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = false; // 游戏由确定性模拟时间显式采样片段。
                var faceError = ValidateFaceClips(extracted, manifest, model, transforms);
                extracted["AW_Idle"].SampleAnimation(model, 0);
                var maxError = ValidateCollisionPose(transforms);
                if (maxError > .008f)
                    throw new InvalidDataException("Unity 蒙皮骨链与确定性碰撞采样偏差超限：" + maxError + " 米。");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                File.WriteAllText(ArtPath + "/unity-import-report.json", JsonUtility.ToJson(new ImportReport
                {
                    meshCount = renderers.Length, boneCount = manifest.boneCount, clipCount = extracted.Count,
                    metersScaleCorrection = scaleCorrection, modelEulerCorrection = model.transform.localEulerAngles,
                    maxIdleJointCoordinateErrorMeters = maxError, maxBlendShapeWeightError = faceError,
                    clipNames = extracted.Keys.OrderBy(n => n).ToArray()
                }, true));
                AssetDatabase.Refresh();
                Debug.Log("深渊凝视者美术已导入：44 个独立蒙皮部件、24 个分区动作、保留已批准头型；场景与玩法配置由专用接入入口处理。");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static AnimationClip ExtractClip(AnimationClip original, Clip spec, Transform root, IDictionary<string, Transform> transforms)
        {
            string path = ClipsPath + "/" + spec.name + ".anim";
            var result = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (result == null)
            {
                result = new AnimationClip();
                AssetDatabase.CreateAsset(result, path);
            }
            EditorUtility.CopySerialized(original, result);
            result.name = spec.name;
            result.frameRate = 30;
            var owned = new HashSet<string>(spec.bones);
            var bindings = AnimationUtility.GetCurveBindings(result);
            var filteredCurves = new AnimationCurve[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                string target = binding.path.Split('/').Last();
                bool keep = binding.type == typeof(Transform) && owned.Contains(target);
                keep |= spec.face && binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);
                filteredCurves[i] = keep ? ReduceCurve(AnimationUtility.GetEditorCurve(result, binding), spec.durationSeconds, .000005f) : null;
            }
            // 一次提交，避免每根曲线触发完整动画缓存重建。
            AnimationUtility.SetEditorCurves(result, bindings, filteredCurves);
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(result))
                AnimationUtility.SetObjectReferenceCurve(result, binding, null);
            AnimationUtility.SetAnimationEvents(result, Array.Empty<AnimationEvent>());
            if (spec.face)
            {
                // FBX 保存完整形态网格；权重按源控制量显式烘焙，避免 Blender driver 被导成常数。
                AddShapeCurve(result, transforms["AW_Head"], root, "Ink_Aperture", spec.mouthOpen);
                AddShapeCurve(result, transforms["AW_MouthCavity"], root, "Ink_Aperture", spec.mouthOpen);
                AddShapeCurve(result, transforms["AW_Head"], root, "Close_Center", spec.blinkCenter);
                AddShapeCurve(result, transforms["AW_Head"], root, "Close_L", spec.blinkL);
                AddShapeCurve(result, transforms["AW_Head"], root, "Close_R", spec.blinkR);
                var curve = LinearCurve(spec.eyeGlow.Select(value => value * .7f).ToArray());
                foreach (string eye in new[] { "Center", "L", "R" })
                {
                    string target = AnimationUtility.CalculateTransformPath(transforms["AW_Eye_" + eye], root);
                    AnimationUtility.SetEditorCurve(result, EditorCurveBinding.FloatCurve(target, typeof(SkinnedMeshRenderer), "material._EmissionStrength"), curve);
                }
            }
            var clipSettings = AnimationUtility.GetAnimationClipSettings(result);
            clipSettings.loopTime = spec.loop;
            clipSettings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(result, clipSettings);
            EditorUtility.SetDirty(result);
            return result;
        }

        private static void AddShapeCurve(AnimationClip clip, Transform target, Transform root, string shape, float[] values)
        {
            if (values == null || values.Length < 2) throw new InvalidDataException("缺少源表情采样：" + clip.name + "/" + shape);
            var mesh = target.GetComponent<SkinnedMeshRenderer>().sharedMesh;
            int index = mesh.GetBlendShapeIndex(shape);
            if (index < 0) throw new InvalidDataException("FBX 缺少形态网格：" + shape);
            string path = AnimationUtility.CalculateTransformPath(target, root);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape),
                LinearCurve(values.Select(v => v * 100).ToArray()));
        }

        private static AnimationCurve LinearCurve(float[] values)
        {
            var input = new AnimationCurve(values.Select((value, i) => new Keyframe(i / 30f, value)).ToArray());
            SetLinear(input);
            return ReduceCurve(input, (values.Length - 1) / 30f, .000005f);
        }

        private static void SetLinear(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
        }

        private static AnimationCurve ReduceCurve(AnimationCurve input, float duration, float tolerance)
        {
            // 保留 30 Hz 原采样的数值精度，仅消除常量和线性冗余点；误差不会进入玩法状态。
            int count = Mathf.RoundToInt(duration * 30) + 1;
            var values = Enumerable.Range(0, count).Select(i => input.Evaluate(i / 30f)).ToArray();
            var keep = new bool[count];
            keep[0] = keep[count - 1] = true;
            var pending = new Stack<Vector2Int>();
            pending.Push(new Vector2Int(0, count - 1));
            while (pending.Count > 0)
            {
                var range = pending.Pop();
                int greatestIndex = -1;
                float greatestError = tolerance;
                for (int i = range.x + 1; i < range.y; i++)
                {
                    float expected = Mathf.Lerp(values[range.x], values[range.y], (i - range.x) / (float)(range.y - range.x));
                    float error = Mathf.Abs(values[i] - expected);
                    if (error > greatestError) { greatestIndex = i; greatestError = error; }
                }
                if (greatestIndex < 0) continue;
                keep[greatestIndex] = true;
                pending.Push(new Vector2Int(range.x, greatestIndex));
                pending.Push(new Vector2Int(greatestIndex, range.y));
            }
            var curve = new AnimationCurve(Enumerable.Range(0, count).Where(i => keep[i]).Select(i => new Keyframe(i / 30f, values[i])).ToArray());
            SetLinear(curve);
            return curve;
        }

        private static float ValidateFaceClips(IDictionary<string, AnimationClip> clips, Manifest manifest, GameObject model, IDictionary<string, Transform> transforms)
        {
            var head = transforms["AW_Head"].GetComponent<SkinnedMeshRenderer>();
            var cavity = transforms["AW_MouthCavity"].GetComponent<SkinnedMeshRenderer>();
            float error = 0;
            foreach (var spec in manifest.clips.Where(c => c.face))
                foreach (int frame in new[] { 0, spec.frameCount / 2, Math.Min(73, spec.frameCount - 1), spec.frameCount - 1 }.Distinct())
                {
                    clips[spec.name].SampleAnimation(model, frame / 30f);
                    error = Mathf.Max(error, Mathf.Abs(head.GetBlendShapeWeight(head.sharedMesh.GetBlendShapeIndex("Ink_Aperture")) - spec.mouthOpen[frame] * 100));
                    error = Mathf.Max(error, Mathf.Abs(cavity.GetBlendShapeWeight(cavity.sharedMesh.GetBlendShapeIndex("Ink_Aperture")) - spec.mouthOpen[frame] * 100));
                    error = Mathf.Max(error, Mathf.Abs(head.GetBlendShapeWeight(head.sharedMesh.GetBlendShapeIndex("Close_Center")) - spec.blinkCenter[frame] * 100));
                    error = Mathf.Max(error, Mathf.Abs(head.GetBlendShapeWeight(head.sharedMesh.GetBlendShapeIndex("Close_L")) - spec.blinkL[frame] * 100));
                    error = Mathf.Max(error, Mathf.Abs(head.GetBlendShapeWeight(head.sharedMesh.GetBlendShapeIndex("Close_R")) - spec.blinkR[frame] * 100));
                }
            if (error > .001f) throw new InvalidDataException("Unity 表情采样与源控制量不一致：" + error);
            return error;
        }

        private static Material GetMaterial(string name, Shader shader)
        {
            string path = MaterialPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.color = Color.white;
            string texture = name == "AW_FaceSkin" ? "AW_Skin" : name;
            material.SetTexture("_MainTex", Texture(texture + "_BaseColor"));
            material.SetTexture("_RoughnessMap", Texture(texture + "_Roughness"));
            bool organic = name != "AW_Eye" && name != "AW_Pupil" && name != "AW_Fissure" && name != "AW_Cavity";
            material.SetTexture("_NormalMap", organic ? Texture("AW_Surface_Normal") : null);
            material.SetTexture("_EmissionMap", name == "AW_Eye" || name == "AW_Fissure" ? Texture(name + "_Emission") : null);
            material.SetFloat("_NormalStrength", organic ? name == "AW_Reef" ? .55f : .24f : 0);
            material.SetFloat("_EmissionStrength", name == "AW_Eye" ? .7f : name == "AW_Fissure" ? .65f : 0);
            material.SetFloat("_TissueWeight", name == "AW_FaceSkin" ? 1 : 0);
            material.SetFloat("_Specular", name == "AW_Eye" ? .012f : .028f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "/Textures/" + name + ".png");

        private static void ConfigureTextures()
        {
            foreach (string path in Directory.GetFiles(ArtPath + "/Textures", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool normal = path.Contains("_Normal");
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !normal && !path.Contains("_Roughness");
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.maxTextureSize = 1024;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        private static float ValidateCollisionPose(IDictionary<string, Transform> transforms)
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../source-assets/creatures/abyssal-watcher/exports/unity"));
            var manifest = JsonUtility.FromJson<CollisionManifest>(File.ReadAllText(Path.Combine(directory, "collision-manifest.json")));
            int offset = manifest.clips.Single(c => c.name == "AW_Idle").offsetBytes;
            using (var stream = File.OpenRead(Path.Combine(directory, "collision-joints.bin")))
            using (var reader = new BinaryReader(stream))
            {
                float error = 0;
                foreach (var limb in manifest.limbs)
                    for (int i = 0; i < limb.pointCount - 1; i++)
                    {
                        stream.Position = offset + (limb.pointOffset + i) * 16;
                        var expected = new Vector3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()) / 1000;
                        var actual = transforms["DEF_" + limb.id + "_" + i.ToString("00")].position;
                        var delta = actual - expected;
                        error = Mathf.Max(error, Mathf.Abs(delta.x), Mathf.Abs(delta.y), Mathf.Abs(delta.z));
                    }
                return error;
            }
        }

        private static Bounds LocalBounds(Transform transform, Bounds world)
        {
            var bounds = new Bounds(transform.InverseTransformPoint(world.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        bounds.Encapsulate(transform.InverseTransformPoint(world.center + Vector3.Scale(world.extents, new Vector3(x, y, z))));
            return bounds;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
