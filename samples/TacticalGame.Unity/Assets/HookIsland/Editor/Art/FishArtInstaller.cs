using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Explicit, repeatable source-art import. Does not rebuild or save gameplay scenes.</summary>
    public static class FishArtInstaller
    {
        public const string ArtPath = "Assets/HookIsland/Art/Fish";
        public const string DefinitionPath = "Assets/HookIsland/Resources/FishVisuals.asset";
        private static readonly string[] PoseNames = Enum.GetNames(typeof(FishPose));

        [Serializable] private sealed class SourceManifest
        {
            public int schema = 0;
            public string art_revision = "";
            public SourceFish[] species = Array.Empty<SourceFish>();
        }
        [Serializable] private sealed class SourceFish
        {
            public string id = "", slug = "";
            public float length_m = 0;
        }

        [MenuItem("Tools/Hook Island/Install Animated Fish")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play before importing fish art.");
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../source-assets/creatures/fish"));
            var manifest = JsonUtility.FromJson<SourceManifest>(File.ReadAllText(Path.Combine(source, "manifest.json")));
            if (manifest.schema != 2 || manifest.species.Length != FishCatalog.All.Count + 1 ||
                !FishCatalog.All.Select(f => f.Id).Concat(new[] { "piranha" }).OrderBy(id => id)
                    .SequenceEqual(manifest.species.Select(f => f.id).OrderBy(id => id)))
                throw new InvalidDataException("Fish art IDs must cover the complete simulation catalogue and piranha.");

            foreach (string folder in new[] { ArtPath + "/Models", ArtPath + "/Textures", ArtPath + "/Materials", ArtPath + "/Prefabs", "Assets/HookIsland/Resources" })
                EnsureFolder(folder);
            // Keep existing .meta files and GUIDs when re-running the importer.
            foreach (var entry in manifest.species)
            {
                CopyIfChanged(Path.Combine(source, "exports", entry.id + "_" + entry.slug + ".fbx"), ModelPath(entry.id));
                CopyIfChanged(Path.Combine(source, "exports/textures", entry.id + "_albedo.png"), TexturePath(entry.id));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<FishVisualCatalog>(DefinitionPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<FishVisualCatalog>();
                AssetDatabase.CreateAsset(asset, DefinitionPath);
            }
            asset.shadowShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/HookIsland/Shaders/FishShadow.shader");
            var entries = manifest.species.ToDictionary(entry => entry.id, Import);
            asset.species = FishCatalog.All.Select(fish => entries[fish.Id]).ToArray();
            asset.piranha = entries["piranha"];
            if (!asset.Complete) throw new InvalidDataException("The imported fish art catalogue is incomplete.");
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets();
            Debug.Log("Animated fish installed: " + manifest.art_revision + ", 11 textured prefabs and 78 in-place clips. Gameplay scenes were not rewritten.");
        }

        private static FishVisualDefinition Import(SourceFish entry)
        {
            string path = ModelPath(entry.id);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = importer.importLights = importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.globalScale = 1; importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.SaveAndReimport();
            var settings = importer.defaultClipAnimations;
            foreach (var clip in settings)
            {
                clip.name = clip.name.Split('|').Last();
                if (!PoseNames.Contains(clip.name)) throw new InvalidDataException("Unexpected fish clip: " + clip.name);
                clip.loopTime = clip.name != "Leap" && clip.name != "Dive" && clip.name != "Bite";
                clip.loopPose = clip.loopTime;
                clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            if (settings.Length != (entry.id == "piranha" ? 8 : 7)) throw new InvalidDataException("Missing fish takes in " + path);
            importer.clipAnimations = settings; importer.SaveAndReimport();

            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(TexturePath(entry.id));
            textureImporter.textureType = TextureImporterType.Default;
            textureImporter.filterMode = FilterMode.Point;
            textureImporter.wrapMode = TextureWrapMode.Clamp;
            textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
            textureImporter.mipmapEnabled = false;
            textureImporter.sRGBTexture = true;
            textureImporter.alphaSource = TextureImporterAlphaSource.None;
            textureImporter.maxTextureSize = 1024;
            textureImporter.SaveAndReimport();

            var shader = Shader.Find("Hook Island/Baked Color Surface");
            if (shader == null) throw new InvalidOperationException("Missing fish surface shader.");
            string materialPath = ArtPath + "/Materials/" + entry.id + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader) { name = entry.id + " Pixel Surface" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = shader; material.color = Color.white;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(entry.id));
            material.SetFloat("_Ambient", .72f); material.SetFloat("_SunStrength", .4f); material.SetFloat("_OcclusionStrength", 0);
            EditorUtility.SetDirty(material);

            var root = new GameObject("Fish visual " + entry.id);
            try
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform, false);
                instance.name = "Model";
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.applyRootMotion = false; animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var bones = instance.GetComponentsInChildren<Transform>(true);
                var head = bones.Single(b => b.name == "Head"); var tail = bones.Single(b => b.name == "Tail");
                var forward = head.position - tail.position; forward.y = 0;
                if (forward.sqrMagnitude < .000001f) throw new InvalidDataException("Invalid fish head/tail axis: " + entry.id);
                instance.transform.rotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward) * instance.transform.rotation;
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.sharedMaterials = new[] { material };
                    renderer.updateWhenOffscreen = true;
                    renderer.localBounds = new Bounds(renderer.localBounds.center, renderer.localBounds.size * 2 + Vector3.one * .1f);
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, ArtPath + "/Prefabs/" + entry.id + ".prefab");
                var animations = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
                AnimationClip Clip(string name) => animations.Single(c => c.name == name);
                return new FishVisualDefinition
                {
                    id = entry.id, model = prefab, baseLengthMetres = entry.length_m,
                    shadowShape = entry.id == "fish_10" ? FishShadowShape.Long :
                        entry.id == "fish_04" || entry.id == "fish_05" || entry.id == "fish_07" ? FishShadowShape.Rounded : FishShadowShape.Streamlined,
                    idle = Clip("Idle"), swim = Clip("Swim"), burst = Clip("Burst"), leap = Clip("Leap"),
                    dive = Clip("Dive"), hooked = Clip("Hooked"), flop = Clip("Flop"), bite = entry.id == "piranha" ? Clip("Bite") : null
                };
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static string ModelPath(string id) => ArtPath + "/Models/" + id + ".fbx";
        private static string TexturePath(string id) => ArtPath + "/Textures/" + id + "_albedo.png";
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }
        private static void CopyIfChanged(string source, string target)
        {
            if (!File.Exists(target) || !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target))) File.Copy(source, target, true);
        }
    }
}
