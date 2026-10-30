using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Explicit, repeatable import of Blender art; never rewrites a gameplay scene or map.</summary>
    public static class FisherCharacterInstaller
    {
        public const string ModelPath = "Assets/HookIsland/Art/Characters/Fisher/Fisher.fbx";
        public const string DefinitionPath = "Assets/HookIsland/Resources/FisherCharacter.asset";
        private const string ArtPath = "Assets/HookIsland/Art/Characters/Fisher";

        [MenuItem("Tools/Hook Island/Install Animated Fisher")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play before importing character art.");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Generate Fisher.fbx using scripts/blender/create_fisher_character.py first.");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.optimizeGameObjects = false; // Bone sockets are used for the rod tip and equipment.
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.SaveAndReimport();
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                int nameStart = clip.name.LastIndexOf("Fisher_", StringComparison.Ordinal);
                if (nameStart < 0) throw new InvalidOperationException("Unexpected fisher animation: " + clip.name);
                clip.name = clip.name.Substring(nameStart);
                clip.loopTime = clip.name != "Fisher_Cast";
                clip.loopPose = clip.loopTime;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            EnsureFolder(ArtPath + "/Materials");
            EnsureFolder("Assets/HookIsland/Resources");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = UnityEngine.Object.Instantiate(source);
            try
            {
                instance.name = "Animated Fisher";
                var shader = Shader.Find("Hook Island/Baked Color Surface");
                if (shader == null) throw new InvalidOperationException("Missing character surface shader.");
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.updateWhenOffscreen = true;
                    // Cast goes behind the head; retain a conservative, fixed cosmetic bound.
                    renderer.localBounds = new Bounds(renderer.transform.InverseTransformPoint(new Vector3(0, .55f, 0)),
                        Vector3.one * (4 / renderer.transform.lossyScale.x));
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        string name = materials[i].name;
                        string path = ArtPath + "/Materials/" + name + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (material == null)
                        {
                            material = new Material(shader) { name = name, color = ColorFor(name) };
                            material.SetFloat("_Ambient", .4f); material.SetFloat("_SunStrength", .85f);
                            AssetDatabase.CreateAsset(material, path);
                        }
                        materials[i] = material;
                    }
                    renderer.sharedMaterials = materials;
                }
                // Keep bone paths intact: the FBX clips animate the rig, not these mesh objects.
                var rod = new GameObject("Equipped fishing rod").transform;
                rod.SetParent(instance.transform, false);
                foreach (string mesh in new[] { "Rod Shaft", "Rod Tip Shaft", "Rod Details" })
                    instance.transform.Find(mesh).SetParent(rod, true);
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, ArtPath + "/Fisher.prefab");
                var asset = AssetDatabase.LoadAssetAtPath<FisherCharacterDefinition>(DefinitionPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<FisherCharacterDefinition>();
                    AssetDatabase.CreateAsset(asset, DefinitionPath);
                }
                var animations = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().ToArray();
                asset.model = prefab;
                asset.idle = animations.Single(c => c.name == "Fisher_Idle");
                asset.walk = animations.Single(c => c.name == "Fisher_Walk");
                asset.cast = animations.Single(c => c.name == "Fisher_Cast");
                asset.reel = animations.Single(c => c.name == "Fisher_Reel");
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                Debug.Log("Animated fisher installed: Blender model, four clips, equipment sockets and runtime definition.");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Color ColorFor(string name)
        {
            switch (name)
            {
                case "Fisher_Coat": return new Color(.12f, .57f, 1);
                case "Fisher_Skin": return new Color(1, .72f, .42f);
                case "Fisher_Boots": return new Color(.10f, .16f, .21f);
                case "Fisher_Cream": return new Color(1, .94f, .76f);
                case "Fisher_Leather": return new Color(.42f, .23f, .10f);
                case "Fisher_Metal": return new Color(.7f, .82f, .89f);
                case "Fisher_Gold": return new Color(1, .72f, .06f);
                case "Fisher_Blush": return new Color(1, .52f, .44f);
                default: return new Color(.035f, .06f, .08f);
            }
        }
    }
}
