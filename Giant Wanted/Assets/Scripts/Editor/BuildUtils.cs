using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GiantWanted.EditorTools
{
    /// <summary>
    /// Grab bag of helpers used by <see cref="GiantWantedBuilder"/>: measuring models,
    /// generating placeholder sprites/materials/particles, and laying out uGUI by hand.
    /// Editor only - nothing here ships in a build.
    /// </summary>
    public static class BuildUtils
    {
        public const string GeneratedArtFolder = "Assets/Art/Generated";
        public const string PrefabFolder = "Assets/Prefabs";

        // ------------------------------------------------------------------ paths

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];                 // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        // ----------------------------------------------------------------- bounds

        /// <summary>
        /// World-space bounds computed from shared meshes rather than Renderer.bounds, so it
        /// works on inactive objects too (the gun and bullet ship disabled in the scene).
        /// </summary>
        public static bool TryGetModelBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Encapsulate(ref bounds, ref any, mf.sharedMesh.bounds, mf.transform.localToWorldMatrix);
            }

            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                Encapsulate(ref bounds, ref any, smr.sharedMesh.bounds, smr.transform.localToWorldMatrix);
            }

            return any;
        }

        static void Encapsulate(ref Bounds bounds, ref bool any, Bounds local, Matrix4x4 matrix)
        {
            Vector3 c = local.center;
            Vector3 e = local.extents;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);

                Vector3 world = matrix.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(world, Vector3.zero); any = true; }
                else bounds.Encapsulate(world);
            }
        }

        /// <summary>Local-space bounds of a model, used to find which axis the barrel runs along.</summary>
        public static bool TryGetLocalBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            Matrix4x4 worldToLocal = root.transform.worldToLocalMatrix;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Encapsulate(ref bounds, ref any, mf.sharedMesh.bounds, worldToLocal * mf.transform.localToWorldMatrix);
            }

            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                Encapsulate(ref bounds, ref any, smr.sharedMesh.bounds, worldToLocal * smr.transform.localToWorldMatrix);
            }

            return any;
        }

        /// <summary>Index (0/1/2) of the largest axis of a bounds.</summary>
        public static int LongestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return 0;
            if (size.y >= size.z) return 1;
            return 2;
        }

        // ------------------------------------------------------------ layers/tags

        public static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            // User layers start at 8; 3, 6 and 7 are technically free but conventionally left alone.
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty element = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(element.stringValue)) continue;

                element.stringValue = name;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return i;
            }

            Debug.LogWarning("[Giant Wanted] No free user layer for '" + name + "'. Using Default.");
            return 0;
        }

        public static void EnsureTag(string tag)
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty tags = tagManager.FindProperty("tags");

            for (int i = 0; i < tags.arraySize; i++)
                if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        // ---------------------------------------------------------------- sprites

        /// <summary>Generate (or reuse) a PNG sprite asset. <paramref name="pixel"/> gets uv in 0..1.</summary>
        public static Sprite CreateSprite(string fileName, int size, Func<float, float, Color> pixel)
        {
            EnsureFolder(GeneratedArtFolder);
            string path = GeneratedArtFolder + "/" + fileName + ".png";

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    pixels[y * size + x] = pixel(u, v);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        public static Color SolidWhite(float u, float v) => Color.white;

        public static Color Circle(float u, float v)
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float a = Mathf.Clamp01(1f - Mathf.InverseLerp(0.92f, 1f, d));
            return new Color(1f, 1f, 1f, a);
        }

        public static Color Ring(float u, float v)
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float a = Mathf.Clamp01(1f - Mathf.InverseLerp(0.92f, 1f, d)) *
                      Mathf.Clamp01(Mathf.InverseLerp(0.72f, 0.80f, d));
            return new Color(1f, 1f, 1f, a);
        }

        public static Color Crosshair(float u, float v)
        {
            // Four ticks around a small centre gap, plus a faint centre dot.
            const float thickness = 0.035f;
            const float gap = 0.11f;
            const float length = 0.42f;

            float x = u - 0.5f;
            float y = v - 0.5f;

            bool vertical = Mathf.Abs(x) < thickness && Mathf.Abs(y) > gap && Mathf.Abs(y) < length;
            bool horizontal = Mathf.Abs(y) < thickness && Mathf.Abs(x) > gap && Mathf.Abs(x) < length;
            bool dot = new Vector2(x, y).magnitude < 0.022f;

            return (vertical || horizontal || dot) ? Color.white : Color.clear;
        }

        public static Color SoftGlow(float u, float v)
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float a = Mathf.Clamp01(1f - d);
            return new Color(1f, 1f, 1f, a * a);
        }

        // -------------------------------------------------------------- materials

        /// <summary>Additive unlit material suitable for particles and tracers.</summary>
        public static Material CreateAdditiveMaterial(string assetName, Texture texture, Color color)
        {
            EnsureFolder(GeneratedArtFolder);
            string path = GeneratedArtFolder + "/" + assetName + ".mat";

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader);

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            // Transparent + additive blending.
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        // -------------------------------------------------------------- particles

        public struct BurstSpec
        {
            public string name;
            public Material material;
            public Color startColor;
            public int burstCount;
            public float lifetime;
            public float speed;
            public float size;
            public float coneAngle;
            public float gravity;
        }

        public static GameObject CreateBurstPrefab(BurstSpec spec)
        {
            EnsureFolder(PrefabFolder);

            GameObject go = new GameObject(spec.name);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(spec.lifetime * 0.6f, spec.lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(spec.speed * 0.4f, spec.speed);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.size * 0.5f, spec.size);
            main.startColor = spec.startColor;
            main.gravityModifier = spec.gravity;
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)spec.burstCount) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spec.coneAngle;
            shape.radius = spec.size * 0.25f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = spec.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            string path = PrefabFolder + "/" + spec.name + ".prefab";
            AssetDatabase.DeleteAsset(path);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        // --------------------------------------------------------------------- UI

        public static Font UIFont
        {
            get
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return font;
            }
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;
            return rt;
        }

        public static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            RectTransform rt = NewRect(name, parent);
            Image image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text NewText(string name, Transform parent, string content, int fontSize,
                                   TextAnchor anchor, Color color, FontStyle style = FontStyle.Bold)
        {
            RectTransform rt = NewRect(name, parent);
            Text text = rt.gameObject.AddComponent<Text>();
            text.font = UIFont;
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            Shadow shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
            shadow.effectDistance = new Vector2(2f, -2f);

            return text;
        }

        public static void DestroyIfExists(params GameObject[] objects)
        {
            foreach (GameObject go in objects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        public static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }
    }
}
