using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Editor
{
    /// <summary>
    /// 从正式渔夫模型烘焙四个席位的 HUD 头像。只在编辑器中显式执行，所有物体位于隔离预览场景。
    /// 输出普通透明 PNG；发布版本不依赖此工具、额外相机或运行时模型渲染。
    /// </summary>
    public static class FisherHudPortraitInstaller
    {
        private const string Folder = "Assets/HookIsland/Resources/UI/Portraits";

        [MenuItem("Tools/Hook Island/Rebuild HUD Portraits")]
        public static void Rebuild()
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先停止运行，再生成 HUD 头像。");
            var definition=Resources.Load<FisherCharacterDefinition>(FisherCharacterDefinition.ResourcePath);
            if (definition==null || !definition.Complete) throw new InvalidOperationException("缺少渔夫模型或待机动作。");
            Directory.CreateDirectory(Folder);
            for (int slot=0;slot<FishingPresentation.PlayerColors.Length;slot++) Render(definition,slot);
            AssetDatabase.Refresh();
            Debug.Log("已从正式渔夫模型生成四个席位的 HUD 头像。");
        }

        private static void Render(FisherCharacterDefinition definition, int slot)
        {
            const int size=256;
            var preview=new PreviewRenderUtility();
            var materials=new List<Material>();
            Texture2D image=null;
            RenderTexture previous=RenderTexture.active;
            try
            {
                var model=preview.InstantiatePrefabInScene(definition.model);
                model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                definition.idle.SampleAnimation(model,0);
                var animator=model.GetComponent<Animator>();if(animator!=null)animator.enabled=false;
                // 头像表现基础角色身份；临时手持物和可装卸护具不参与构图。
                foreach (string name in new[]{"Equipped fishing rod","Backpack","Equipped cap guard","Equipped reinforced vest"})
                {var child=model.transform.Find(name);if(child!=null)child.gameObject.SetActive(false);}
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    var shared=renderer.sharedMaterials;
                    for (int index=0;index<shared.Length;index++)
                    {
                        var material=new Material(shared[index]);materials.Add(material);
                        if (material.name.StartsWith("Fisher_Coat",StringComparison.Ordinal)) material.color=FishingPresentation.PlayerColors[slot];
                        if (material.HasProperty("_Ambient")) material.SetFloat("_Ambient",.58f);
                        shared[index]=material;
                    }
                    renderer.sharedMaterials=shared;
                }
                preview.camera.orthographic=true;
                preview.camera.orthographicSize=.31f;
                preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=10;
                preview.camera.clearFlags=CameraClearFlags.SolidColor;
                preview.camera.backgroundColor=Color.clear;
                preview.camera.transform.position=new Vector3(.65f,.94f,1.7f);
                preview.camera.transform.LookAt(new Vector3(0,.82f,.06f));
                preview.lights[0].intensity=1.1f;
                preview.lights[0].transform.rotation=Quaternion.Euler(35,215,0);
                preview.lights[1].intensity=.4f;
                preview.lights[1].transform.rotation=Quaternion.Euler(340,135,0);
                // 预览窗口使用逻辑像素。Retina 下直接传 256 会得到 512 贴图，
                // 再只读取左下 256 像素就会把头像裁掉；显式换算，并读取实际渲染尺寸。
                float logicalSize=size/EditorGUIUtility.pixelsPerPoint;
                preview.BeginPreview(new Rect(0,0,logicalSize,logicalSize),GUIStyle.none);
                preview.Render(false,false);
                var rendered=(RenderTexture)preview.EndPreview();RenderTexture.active=rendered;
                image=new Texture2D(rendered.width,rendered.height,TextureFormat.RGBA32,false);
                image.ReadPixels(new Rect(0,0,rendered.width,rendered.height),0,0);image.Apply();
                string path=Folder+"/FisherP"+(slot+1)+".png";
                File.WriteAllBytes(path,image.EncodeToPNG());
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;
                importer.alphaIsTransparency=true;importer.mipmapEnabled=false;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
                importer.maxTextureSize=size;importer.SaveAndReimport();
            }
            finally
            {
                RenderTexture.active=previous;
                if (image!=null)Object.DestroyImmediate(image);
                preview.Cleanup();
                foreach (var material in materials)Object.DestroyImmediate(material);
            }
        }
    }
}
