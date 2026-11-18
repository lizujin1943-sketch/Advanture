using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Repeatable Blender import with stable asset GUIDs and explicit catalogue validation.</summary>
    public static class EldritchArtInstaller
    {
        public const string ArtPath = "Assets/HookIsland/Art/Eldritch";
        public const string CatalogPath = "Assets/HookIsland/Resources/EldritchVisuals.asset";
        private static readonly string[] Poses = Enum.GetNames(typeof(EldritchPose));
        private static readonly string[] SurfaceNames = { "Skin", "Shell", "Ivory", "Flesh", "Cavity", "Lumen" };

        [Serializable] private sealed class Manifest
        {
            public int schema;
            public Entry[] species;
        }
        [Serializable] private sealed class Entry
        {
            public int kind;
            public string name;
            public float[] color;
        }

        [MenuItem("Tools/Hook Island/Install Animated Eldritch")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play before importing enemy art.");
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../source-assets/creatures/eldritch"));
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(source, "manifest.json")));
            if (manifest.schema != 1 || manifest.species == null || manifest.species.Length != EldritchCatalog.SpeciesCount)
                throw new InvalidDataException("Enemy art must cover the complete simulation catalogue.");
            for (int i = 0; i < manifest.species.Length; i++)
                if (manifest.species[i].kind != i || manifest.species[i].name != ((EldritchKind)i).ToString())
                    throw new InvalidDataException("Enemy art IDs are out of order.");
            foreach (string folder in new[] { "Models", "Textures", "Materials", "Prefabs" })
                Directory.CreateDirectory(ArtPath + "/" + folder);
            Directory.CreateDirectory("Assets/HookIsland/Resources");
            foreach (var entry in manifest.species)
                Copy(Path.Combine(source, "exports", entry.name + ".fbx"), ModelPath(entry.name));
            foreach (var path in Directory.GetFiles(Path.Combine(source, "exports/textures"), "*.png"))
                Copy(path, ArtPath + "/Textures/" + Path.GetFileName(path));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtPath + "/Textures" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = path.Contains("Normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = path.Contains("Albedo"); importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat; importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }
            var definitions = manifest.species.Select(Import).ToArray();
            var catalog = AssetDatabase.LoadAssetAtPath<EldritchVisualCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<EldritchVisualCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.species = definitions;
            if (!catalog.Complete) throw new InvalidDataException("Incomplete enemy art catalogue.");
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Debug.Log("ELDRITCH_ART_OK species=10 clips=80 source=Blender in_place=true");
        }

        private static string ModelPath(string name) => ArtPath + "/Models/" + name + ".fbx";
        public static void InstallAndCapture()
        {
            Install();
            EldritchBestiaryWindow.ExportAnimatedPortraits();
        }
        private static void Copy(string source, string destination)
        {
            var bytes = File.ReadAllBytes(source);
            if (!File.Exists(destination) || !File.ReadAllBytes(destination).SequenceEqual(bytes))
                File.WriteAllBytes(destination, bytes);
        }

        private static EldritchVisualDefinition Import(Entry entry)
        {
            string path = ModelPath(entry.name);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true; importer.importCameras = false; importer.importLights = false;
            importer.addCollider = false; importer.optimizeGameObjects = false;
            importer.globalScale = 1; importer.useFileScale = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();
            var settings = importer.defaultClipAnimations;
            foreach (var clip in settings)
            {
                int start = clip.name.LastIndexOf(entry.name + "_", StringComparison.Ordinal);
                if (start < 0) throw new InvalidDataException("Unexpected animation take: " + clip.name);
                clip.name = clip.name.Substring(start + entry.name.Length + 1);
                clip.loopTime = clip.name == "Idle" || clip.name == "Move";
                clip.loopPose = clip.loopTime;
                clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            if (!settings.Select(c => c.name).OrderBy(n => n).SequenceEqual(Poses.OrderBy(n => n)))
                throw new InvalidDataException("Missing or duplicate animation takes: " + entry.name);
            importer.clipAnimations = settings; importer.SaveAndReimport();
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            var root = new GameObject(entry.name);
            try
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform, false);
                instance.name = "Model";
                var bones = instance.GetComponentsInChildren<Transform>(true);
                var forward = bones.Single(t => t.name == "Facing").position - bones.Single(t => t.name == "Root").position;
                forward.y = 0;
                instance.transform.rotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward) * instance.transform.rotation;
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var materials = Enumerable.Range(0, SurfaceNames.Length).Select(slot => Material(entry, slot)).ToArray();
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    // Unity may reorder FBX submeshes. Match their authored material names.
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
                        materials.Single(material => material.name == source.name)).ToArray();
                    // The project's lowest quality tier otherwise truncates these blended joints to one bone.
                    renderer.quality = SkinQuality.Bone2;
                    renderer.updateWhenOffscreen = true;
                    renderer.localBounds = new Bounds(renderer.localBounds.center,
                        renderer.localBounds.size * 1.6f + Vector3.one * (.5f / renderer.transform.lossyScale.x));
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, ArtPath + "/Prefabs/" + entry.name + ".prefab");
                return new EldritchVisualDefinition { kind = (EldritchKind)entry.kind, model = prefab,
                    clips = Poses.Select(pose => clips.Single(c => c.name == pose)).ToArray() };
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Material Material(Entry entry, int slot)
        {
            var baseColor = new Color(entry.color[0], entry.color[1], entry.color[2]);
            Color[] colors = { baseColor, baseColor * .6f, new Color(.28f,.26f,.20f),
                new Color(.14f,.055f,.05f), new Color(.008f,.012f,.012f),
                entry.kind == 5 ? new Color(.29f,.45f,.055f) : new Color(.055f,.36f,.23f) };
            string name = entry.name + "_" + SurfaceNames[slot];
            string path = ArtPath + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Missing Standard surface shader.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader; material.color = colors[slot].gamma;
            material.SetFloat("_Glossiness", slot == 3 || slot == 5 ? .72f : .48f);
            material.SetFloat("_Metallic", 0);
            if (slot < 4)
            {
                material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "/Textures/AbyssSkin_Albedo.png");
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "/Textures/AbyssSkin_Normal.png"));
                material.SetFloat("_BumpScale", .55f); material.EnableKeyword("_NORMALMAP");
                material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "/Textures/AbyssSkin_MetallicSmoothness.png"));
                material.SetFloat("_GlossMapScale", 1); material.EnableKeyword("_METALLICGLOSSMAP");
            }
            if (slot == 5)
            {
                material.SetColor("_EmissionColor", colors[slot] * .65f); material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
