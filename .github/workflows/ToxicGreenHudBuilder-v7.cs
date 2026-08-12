#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PHPVE
{
    public static class ToxicGreenHudBuilder
    {
        public const string EffectGuid = "f86ba8f388f04901b50f8b0fe90eebe2";
        public const ushort EffectId = 65041;
        public const string BundleName = "phpve_toxic_green_hud.masterbundle";
        public const string BundleRoot = "Assets/PHPVEToxicGreenHUDMasterBundle";
        public const string EffectFolder = BundleRoot + "/Effects/PHPVEToxicGreenHUD";
        public const string PrefabPath = EffectFolder + "/Effect.prefab";
        public const string TextureFolder = EffectFolder + "/Textures";
        public const string ClipPath = EffectFolder + "/HeartbeatPulse.anim";
        public const string ControllerPath = EffectFolder + "/HeartbeatPulse.controller";

        private static readonly Color Toxic = new Color32(124, 255, 0, 255);
        private static readonly Color ToxicDim = new Color32(73, 138, 20, 255);
        private static readonly Color Panel = new Color32(4, 11, 6, 220);
        private static readonly Color Panel2 = new Color32(8, 18, 10, 235);
        private static readonly Color White = new Color32(232, 240, 228, 255);
        private static readonly Color Muted = new Color32(140, 157, 134, 255);
        private static readonly Color Danger = new Color32(255, 55, 45, 255);
        private static readonly Color Amber = new Color32(255, 190, 30, 255);

        private static Type imageType;
        private static Type textType;
        private static Type outlineType;
        private static Type canvasScalerType;
        private static Type rectMask2DType;

        [MenuItem("Tools/PH PVE/Toxic Green HUD/Create or Refresh UI Source")]
        public static void CreateOrRefresh()
        {
            ResolveUiTypes();
            EnsureDirectories();
            ConfigureTextureImporters();
            CreateHeartbeatAnimation();
            CreateHudPrefab();
            AssignMasterBundle();
            WriteWorkshopDataFiles();
            ValidatePrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PH PVE Toxic Green HUD] UI source created/refreshed successfully.");
        }

        [MenuItem("Tools/PH PVE/Toxic Green HUD/Build RELEASE Workshop (Win + Linux + Mac)")]
        public static void BuildRelease()
        {
            CreateOrRefresh();

            string outputRoot = GetWorkshopOutputRoot();
            string bundlesOutput = Path.Combine(outputRoot, "Bundles");
            Directory.CreateDirectory(bundlesOutput);

            InvokeOfficialU3Exporter(BundleName, bundlesOutput, true);

            WriteWorkshopDataFiles();
            ValidateBuildOutput(outputRoot);

            Debug.Log("[PH PVE Toxic Green HUD] RELEASE Workshop build complete: " + outputRoot);
        }

        [MenuItem("Tools/PH PVE/Toxic Green HUD/Build FAST Local Test (current platform only)")]
        public static void BuildLocalTest()
        {
            CreateOrRefresh();

            string outputRoot = GetWorkshopOutputRoot();
            string bundlesOutput = Path.Combine(outputRoot, "Bundles");
            Directory.CreateDirectory(bundlesOutput);

            InvokeOfficialU3Exporter(BundleName, bundlesOutput, false);

            WriteWorkshopDataFiles();
            Debug.Log("[PH PVE Toxic Green HUD] Local test build complete: " + outputRoot);
        }

        private static void ResolveUiTypes()
        {
            imageType = FindLoadedType("UnityEngine.UI.Image");
            textType = FindLoadedType("UnityEngine.UI.Text");
            outlineType = FindLoadedType("UnityEngine.UI.Outline");
            canvasScalerType = FindLoadedType("UnityEngine.UI.CanvasScaler");
            rectMask2DType = FindLoadedType("UnityEngine.UI.RectMask2D");

            if (imageType == null || textType == null || outlineType == null || canvasScalerType == null || rectMask2DType == null)
            {
                throw new Exception(
                    "Unity UI (uGUI) could not be loaded by the HUD builder. " +
                    "The official U3-SDK Packages/manifest.json includes com.unity.ugui. " +
                    "Close and reopen the U3-SDK project once, then retry. If this persists, open Window > Package Manager and verify Unity UI is installed.");
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            // First check assemblies Unity has already loaded into the editor AppDomain.
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type found = assemblies[i].GetType(fullName, false);
                if (found != null)
                    return found;
            }

            // Package assemblies can exist in the project without being eagerly loaded into
            // the AppDomain yet. uGUI is one of those assemblies in a fresh U3-SDK editor
            // session, so explicitly request it before concluding that it is unavailable.
            if (fullName.StartsWith("UnityEngine.UI.", StringComparison.Ordinal))
            {
                try
                {
                    Assembly uiAssembly = Assembly.Load("UnityEngine.UI");
                    if (uiAssembly != null)
                    {
                        Type found = uiAssembly.GetType(fullName, false);
                        if (found != null)
                            return found;
                    }
                }
                catch
                {
                    // Fall through to Type.GetType below so the final diagnostic remains clear.
                }

                try
                {
                    Type found = Type.GetType(fullName + ", UnityEngine.UI", false);
                    if (found != null)
                        return found;
                }
                catch
                {
                    // ResolveUiTypes will produce the user-facing error if this still fails.
                }
            }

            return null;
        }

        private static void InvokeOfficialU3Exporter(string bundleName, string outputPath, bool multiplatform)
        {
            // Mirror the current U3-SDK EditorAssetBundleHelper.Build implementation directly
            // rather than using reflection to reach an editor-only assembly. This keeps the
            // output format identical while avoiding assembly visibility issues.
            string[] assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
            if (assetPaths == null || assetPaths.Length < 1)
                throw new Exception("No assets are assigned to bundle: " + bundleName);

            Directory.CreateDirectory(outputPath);

            BuildAssetBundleOptions options =
                BuildAssetBundleOptions.DisableLoadAssetByFileName |
                BuildAssetBundleOptions.DisableLoadAssetByFileNameWithExtension;

            BuildTarget editorTarget;
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    editorTarget = BuildTarget.StandaloneWindows64;
                    break;
                case RuntimePlatform.LinuxEditor:
                    editorTarget = BuildTarget.StandaloneLinux64;
                    break;
                case RuntimePlatform.OSXEditor:
                    editorTarget = BuildTarget.StandaloneOSX;
                    break;
                default:
                    throw new Exception("Unsupported Unity editor platform for masterbundle export: " + Application.platform);
            }

            if (multiplatform)
            {
                BuildBundleForTarget(bundleName, assetPaths, outputPath, BuildTarget.StandaloneLinux64, options, editorTarget);
                BuildBundleForTarget(bundleName, assetPaths, outputPath, BuildTarget.StandaloneOSX, options, editorTarget);
                BuildBundleForTarget(bundleName, assetPaths, outputPath, BuildTarget.StandaloneWindows64, options, editorTarget);
            }

            // Always build the editor's current platform last, matching the official U3-SDK helper.
            BuildBundleForTarget(bundleName, assetPaths, outputPath, editorTarget, options, null);

            CleanupAfterBuildingAssetBundle(outputPath);
            HashAssetBundle(Path.Combine(outputPath, bundleName));
            AssetDatabase.Refresh();
        }

        private static void BuildBundleForTarget(
            string bundleName,
            string[] assetPaths,
            string outputPath,
            BuildTarget target,
            BuildAssetBundleOptions options,
            BuildTarget? skipTarget)
        {
            if (skipTarget.HasValue && target == skipTarget.Value)
                return;

            string targetBundleName = GetBuildTargetAssetBundleName(bundleName, target);
            AssetBundleBuild[] builds = new AssetBundleBuild[1];
            builds[0].assetBundleName = targetBundleName;
            builds[0].assetNames = assetPaths;

            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(outputPath, builds, options, target);
            if (manifest == null)
                throw new Exception("Unity failed to build asset bundle '" + targetBundleName + "' for " + target + ".");
        }

        private static string GetBuildTargetAssetBundleName(string bundleName, BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneLinux64:
                    return InsertAssetBundleNameSuffix(bundleName, "_linux");
                case BuildTarget.StandaloneOSX:
                    return InsertAssetBundleNameSuffix(bundleName, "_mac");
                default:
                    return bundleName;
            }
        }

        private static string InsertAssetBundleNameSuffix(string name, string suffix)
        {
            int index = name.IndexOf('.');
            return index < 0 ? name + suffix : name.Insert(index, suffix);
        }

        private static void CleanupAfterBuildingAssetBundle(string outputPath)
        {
            string directoryName = new DirectoryInfo(outputPath).Name;
            string emptyBundlePath = Path.Combine(outputPath, directoryName);
            if (File.Exists(emptyBundlePath))
                File.Delete(emptyBundlePath);

            string emptyManifestPath = emptyBundlePath + ".manifest";
            if (File.Exists(emptyManifestPath))
                File.Delete(emptyManifestPath);
        }

        private static void HashAssetBundle(string windowsFilePath)
        {
            string linuxFilePath = InsertAssetBundleNameSuffix(windowsFilePath, "_linux");
            string macFilePath = InsertAssetBundleNameSuffix(windowsFilePath, "_mac");
            string hashFilePath = windowsFilePath + ".hash";

            // Local test builds intentionally contain only the current platform, so no
            // multiplatform integrity hash is created until the RELEASE build.
            if (!File.Exists(windowsFilePath) || !File.Exists(linuxFilePath) || !File.Exists(macFilePath))
            {
                if (File.Exists(hashFilePath))
                    File.Delete(hashFilePath);
                return;
            }

            using (SHA1 hashAlgo = SHA1.Create())
            {
                byte[] windowsHash = hashAlgo.ComputeHash(File.ReadAllBytes(windowsFilePath));
                byte[] linuxHash = hashAlgo.ComputeHash(File.ReadAllBytes(linuxFilePath));
                byte[] macHash = hashAlgo.ComputeHash(File.ReadAllBytes(macFilePath));

                byte[] hashes = new byte[61];
                hashes[0] = 2;
                Array.Copy(windowsHash, 0, hashes, 1, 20);
                Array.Copy(linuxHash, 0, hashes, 21, 20);
                Array.Copy(macHash, 0, hashes, 41, 20);
                File.WriteAllBytes(hashFilePath, hashes);
            }
        }

        private static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        private static string GetWorkshopOutputRoot()
        {
            return Path.Combine(ProjectRoot, "PH_PVE_Toxic_Green_HUD_Workshop");
        }

        private static void EnsureDirectories()
        {
            Directory.CreateDirectory(Path.Combine(ProjectRoot, BundleRoot.Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(Path.Combine(ProjectRoot, EffectFolder.Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(Path.Combine(ProjectRoot, TextureFolder.Replace('/', Path.DirectorySeparatorChar)));
            AssetDatabase.Refresh();
        }

        private static void ConfigureTextureImporters()
        {
            string[] names =
            {
                "health.png", "food.png", "water.png", "infection.png", "stamina.png",
                "oxygen.png", "bleeding.png", "bone.png", "temperature.png", "warning.png",
                "xp.png", "kills.png", "deaths.png", "rank.png", "helmet.png", "vest.png",
                "top.png", "bottom.png", "primary.png", "secondary.png", "hotkey.png", "ecg.png",
                "scanlines.png", "solid.png"
            };

            foreach (string file in names)
            {
                string path = TextureFolder + "/" + file;
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning("[PH PVE Toxic Green HUD] Missing texture: " + path);
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;

                // Unity 2022.3 exposes spriteMeshType through TextureImporterSettings,
                // not directly on TextureImporter.
                TextureImporterSettings textureSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(textureSettings);
                textureSettings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(textureSettings);

                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = (file == "scanlines.png" || file == "ecg.png") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        private static Sprite LoadSprite(string name)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TextureFolder + "/" + name + ".png");
            if (sprite == null)
                Debug.LogWarning("[PH PVE Toxic Green HUD] Sprite not found: " + name);
            return sprite;
        }

        private static void CreateHeartbeatAnimation()
        {
            CreateHeartbeatClip("HeartbeatNormal", 1.05f, EffectFolder + "/HeartbeatNormal.anim", EffectFolder + "/HeartbeatNormal.controller");
            CreateHeartbeatClip("HeartbeatFast", 0.72f, EffectFolder + "/HeartbeatFast.anim", EffectFolder + "/HeartbeatFast.controller");
            CreateHeartbeatClip("HeartbeatCritical", 0.48f, EffectFolder + "/HeartbeatCritical.anim", EffectFolder + "/HeartbeatCritical.controller");
        }

        private static void CreateHeartbeatClip(string name, float duration, string clipPath, string controllerPath)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null)
                AssetDatabase.DeleteAsset(clipPath);
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
                AssetDatabase.DeleteAsset(controllerPath);

            AnimationClip clip = new AnimationClip
            {
                name = name,
                frameRate = 60,
                wrapMode = WrapMode.Loop
            };

            AnimationCurve scroll = AnimationCurve.Linear(0f, 0f, duration, -256f);
            clip.SetCurve("", typeof(RectTransform), "m_AnchoredPosition.x", scroll);
            AssetDatabase.CreateAsset(clip, clipPath);

            SerializedObject so = new SerializedObject(clip);
            SerializedProperty settings = so.FindProperty("m_AnimationClipSettings");
            if (settings != null)
            {
                SerializedProperty loop = settings.FindPropertyRelative("m_LoopTime");
                if (loop != null)
                {
                    loop.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, clip);
            AssetDatabase.SaveAssets();
        }

        private static void CreateHudPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
                AssetDatabase.DeleteAsset(PrefabPath);

            GameObject root = new GameObject("Effect", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;

            Component scaler = root.AddComponent(canvasScalerType);
            SetEnumMember(scaler, "uiScaleMode", "ScaleWithScreenSize");
            SetMember(scaler, "referenceResolution", new Vector2(1920, 1080));
            SetEnumMember(scaler, "screenMatchMode", "MatchWidthOrHeight");
            SetMember(scaler, "matchWidthOrHeight", 0.5f);
            Stretch(root.GetComponent<RectTransform>());

            GameObject hudFrame = CreateImageObject("HudFrame", root.transform, new Color32(3, 10, 6, 218), null);
            RectTransform frameRt = hudFrame.GetComponent<RectTransform>();
            SetRect(frameRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 10f), new Vector2(1884f, 218f));
            AddOutline(hudFrame, ToxicDim, new Vector2(1.2f, -1.2f));
            AddHeader(hudFrame.transform);
            AddScanlines(hudFrame.transform);

            CreateSurvivalPanel(hudFrame.transform);
            CreateIntelPanel(hudFrame.transform);
            CreateLoadoutPanel(hudFrame.transform);
            CreateHotkeyStrip(hudFrame.transform);
            CreateOxygenAlert(root.transform);

            CreateCornerBracket(hudFrame.transform, true);
            CreateCornerBracket(hudFrame.transform, false);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void AddHeader(Transform parent)
        {
            GameObject title = CreateTextObject("HudTitle", parent, "PH PVE // BIO-MONITOR", 15, FontStyle.Bold, Toxic);
            SetRect(title.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -5f), new Vector2(360f, 22f));
            SetTextAlignment(title, TextAnchor.MiddleLeft);

            GameObject status = CreateTextObject("HudSystemState", parent, "OUTBREAK TELEMETRY // LIVE", 11, FontStyle.Normal, Muted);
            SetRect(status.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-12f, -5f), new Vector2(330f, 22f));
            SetTextAlignment(status, TextAnchor.MiddleRight);

            GameObject line = CreateImageObject("HeaderLine", parent, ToxicDim, null);
            SetRect(line.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -29f), new Vector2(-20f, 1.3f));
        }

        private static void AddScanlines(Transform parent)
        {
            GameObject go = CreateImageObject("Scanlines", parent, new Color(0.35f, 1f, 0.1f, 0.035f), LoadSprite("scanlines"));
            SetEnumMember(GetUiComponent(go, imageType), "type", "Tiled");
            Stretch(go.GetComponent<RectTransform>(), 3f);
            go.transform.SetAsLastSibling();
        }

        private static void CreateSurvivalPanel(Transform parent)
        {
            GameObject panel = CreateImageObject("SurvivalPanel", parent, new Color32(7, 18, 10, 235), null);
            SetRect(panel.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(8f, 8f), new Vector2(856f, 172f));
            AddOutline(panel, new Color(ToxicDim.r, ToxicDim.g, ToxicDim.b, 0.75f), new Vector2(1f, -1f));

            float x = 7f;
            CreateCompactStat(panel.transform, "Health", "HEALTH", LoadSprite("health"), x, 92f, 164f, "100", "HealthCritical", Danger, "CRITICAL"); x += 169f;
            CreateCompactStat(panel.transform, "Food", "HUNGER", LoadSprite("food"), x, 92f, 164f, "100", "FoodWarning", Amber, "LOW"); x += 169f;
            CreateCompactStat(panel.transform, "Water", "THIRST", LoadSprite("water"), x, 92f, 164f, "100", "WaterWarning", Amber, "LOW"); x += 169f;
            CreateCompactStat(panel.transform, "Infection", "INFECTION", LoadSprite("infection"), x, 92f, 164f, "0", "InfectionWarning", Danger, "BIOHAZARD"); x += 169f;
            CreateCompactStat(panel.transform, "Stamina", "STAMINA", LoadSprite("stamina"), x, 92f, 164f, "100", null, Toxic, null);

            CreateEcg(panel.transform);
            CreateConditionStrip(panel.transform);
        }

        private static void CreateCompactStat(Transform parent, string prefix, string label, Sprite icon, float x, float y, float width,
            string initialValue, string warningName, Color warningColor, string warningText)
        {
            GameObject card = CreateImageObject(prefix + "Card", parent, new Color(0f, 0f, 0f, 0.26f), null);
            SetRect(card.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(x, y), new Vector2(width, 72f));

            GameObject iconGo = CreateImageObject(prefix + "Icon", card.transform, Toxic, icon);
            SetRect(iconGo.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(7f, -2f), new Vector2(27f, 27f));
            SetMember(GetUiComponent(iconGo, imageType), "preserveAspect", true);

            GameObject labelText = CreateTextObject(prefix + "Label", card.transform, label, 10, FontStyle.Bold, Toxic);
            SetRect(labelText.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(39f, -7f), new Vector2(width - 44f, 16f));
            SetTextAlignment(labelText, TextAnchor.MiddleLeft);

            GameObject valueText = CreateTextObject(prefix + "Value", card.transform, initialValue, 26, FontStyle.Bold, White);
            SetRect(valueText.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-8f, 1f), new Vector2(72f, 34f));
            SetTextAlignment(valueText, TextAnchor.MiddleRight);

            CreateSegments(card.transform, prefix, width, 7f, 7f);

            if (!string.IsNullOrEmpty(warningName))
            {
                GameObject warning = CreateImageObject(warningName, card.transform, new Color(warningColor.r, warningColor.g, warningColor.b, 0.16f), null);
                Stretch(warning.GetComponent<RectTransform>(), 1f);
                GameObject wt = CreateTextObject(warningName + "Text", warning.transform, warningText, 9, FontStyle.Bold, warningColor);
                SetRect(wt.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-5f, -4f), new Vector2(72f, 14f));
                SetTextAlignment(wt, TextAnchor.MiddleRight);
                warning.SetActive(false);
            }
        }

        private static void CreateSegments(Transform parent, string prefix, float cardWidth, float left, float bottom)
        {
            float available = cardWidth - 14f;
            float gap = 2f;
            float segWidth = (available - gap * 9f) / 10f;
            for (int i = 1; i <= 10; i++)
            {
                GameObject seg = CreateImageObject(prefix + "Seg" + i, parent, Toxic, null);
                SetRect(seg.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(left + (i - 1) * (segWidth + gap), bottom), new Vector2(segWidth, 5f));
            }
        }

        private static void CreateEcg(Transform parent)
        {
            GameObject mask = new GameObject("ECGMask", typeof(RectTransform));
            mask.transform.SetParent(parent, false);
            mask.AddComponent(rectMask2DType);
            SetRect(mask.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(7f, 48f), new Vector2(842f, 37f));

            CreateEcgLayer(mask.transform, "ECGNormal", Toxic, "HeartbeatNormal.controller", true);
            CreateEcgLayer(mask.transform, "ECGFast", new Color32(190, 255, 50, 255), "HeartbeatFast.controller", false);
            CreateEcgLayer(mask.transform, "ECGCritical", Danger, "HeartbeatCritical.controller", false);
        }

        private static void CreateEcgLayer(Transform parent, string name, Color color, string controllerFile, bool active)
        {
            GameObject line = CreateImageObject(name, parent, color, LoadSprite("ecg"));
            Component img = GetUiComponent(line, imageType);
            SetEnumMember(img, "type", "Tiled");
            RectTransform rt = line.GetComponent<RectTransform>();
            SetRect(rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(1120f, 32f));
            Animator animator = line.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(EffectFolder + "/" + controllerFile);
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            line.SetActive(active);
        }

        private static void CreateConditionStrip(Transform parent)
        {
            CreateConditionChip(parent, "Bleeding", "BLEED", "STABLE", LoadSprite("bleeding"), 7f, 8f, 202f, Toxic);
            CreateConditionChip(parent, "Broken", "BONES", "STABLE", LoadSprite("bone"), 214f, 8f, 202f, Toxic);
            CreateTemperatureChip(parent, 421f, 8f, 210f);
            CreateConditionChip(parent, "Status", "SYSTEM", "NOMINAL", LoadSprite("warning"), 636f, 8f, 213f, Toxic);

            GameObject bw = CreateImageObject("BleedingWarning", parent, new Color(Danger.r, Danger.g, Danger.b, 0.18f), null);
            SetRect(bw.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(7f, 8f), new Vector2(202f, 32f));
            bw.SetActive(false);
            GameObject br = CreateImageObject("BrokenWarning", parent, new Color(Amber.r, Amber.g, Amber.b, 0.18f), null);
            SetRect(br.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(214f, 8f), new Vector2(202f, 32f));
            br.SetActive(false);
        }

        private static void CreateConditionChip(Transform parent, string prefix, string label, string initial, Sprite icon, float x, float y, float w, Color color)
        {
            GameObject chip = CreateImageObject(prefix + "Status", parent, new Color(0f, 0f, 0f, 0.24f), null);
            SetRect(chip.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, y), new Vector2(w, 32f));
            GameObject ic = CreateImageObject(prefix + "StatusIcon", chip.transform, color, icon);
            SetRect(ic.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(20f, 20f));
            SetMember(GetUiComponent(ic, imageType), "preserveAspect", true);
            GameObject lt = CreateTextObject(prefix + "StatusLabel", chip.transform, label, 9, FontStyle.Bold, Muted);
            SetRect(lt.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(29f, 0f), new Vector2(62f, 18f));
            SetTextAlignment(lt, TextAnchor.MiddleLeft);
            GameObject val = CreateTextObject(prefix + "Value", chip.transform, initial, 10, FontStyle.Bold, White);
            SetRect(val.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-7f, 0f), new Vector2(w - 92f, 18f));
            SetTextAlignment(val, TextAnchor.MiddleRight);
        }

        private static void CreateTemperatureChip(Transform parent, float x, float y, float w)
        {
            GameObject chip = CreateImageObject("TemperatureStatus", parent, new Color(0f, 0f, 0f, 0.24f), null);
            SetRect(chip.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, y), new Vector2(w, 32f));

            GameObject label = CreateTextObject("TemperatureStatusLabel", chip.transform, "TEMP", 9, FontStyle.Bold, Muted);
            SetRect(label.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(29f, 0f), new Vector2(58f, 18f));
            SetTextAlignment(label, TextAnchor.MiddleLeft);

            CreateTemperatureVariant(chip.transform, "Normal", Toxic, true, w);
            CreateTemperatureVariant(chip.transform, "Warm", new Color32(255, 156, 51, 255), false, w);
            CreateTemperatureVariant(chip.transform, "Cold", new Color32(99, 215, 255, 255), false, w);
        }

        private static void CreateTemperatureVariant(Transform parent, string suffix, Color color, bool active, float w)
        {
            GameObject icon = CreateImageObject("TemperatureIcon" + suffix, parent, color, LoadSprite("temperature"));
            SetRect(icon.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(20f, 20f));
            SetMember(GetUiComponent(icon, imageType), "preserveAspect", true);
            icon.SetActive(active);

            GameObject value = CreateTextObject("TemperatureValue" + suffix, parent, "NORMAL", 10, FontStyle.Bold, color);
            SetRect(value.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-7f, 0f), new Vector2(w - 92f, 18f));
            SetTextAlignment(value, TextAnchor.MiddleRight);
            value.SetActive(active);
        }

        private static void CreateIntelPanel(Transform parent)
        {
            GameObject panel = CreateImageObject("IntelPanel", parent, new Color32(7, 18, 10, 235), null);
            SetRect(panel.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(872f, 52f), new Vector2(512f, 128f));
            AddOutline(panel, new Color(ToxicDim.r, ToxicDim.g, ToxicDim.b, 0.75f), new Vector2(1f, -1f));

            CreateIntelCell(panel.transform, "XP", "XP", "0", LoadSprite("xp"), 6f, 64f, 122f);
            CreateIntelCell(panel.transform, "Kills", "KILLS", "0", LoadSprite("kills"), 132f, 64f, 122f);
            CreateIntelCell(panel.transform, "Deaths", "DEATHS", "0", LoadSprite("deaths"), 258f, 64f, 122f);
            CreateIntelCell(panel.transform, "Rank", "RANK", "SURVIVOR", LoadSprite("rank"), 384f, 64f, 122f);

            CreateGearCell(panel.transform, "Helmet", "HELMET", LoadSprite("helmet"), 6f, 7f, 122f);
            CreateGearCell(panel.transform, "Vest", "VEST", LoadSprite("vest"), 132f, 7f, 122f);
            CreateGearCell(panel.transform, "Top", "TOP", LoadSprite("top"), 258f, 7f, 122f);
            CreateGearCell(panel.transform, "Bottom", "BOTTOM", LoadSprite("bottom"), 384f, 7f, 122f);
        }

        private static void CreateIntelCell(Transform parent, string prefix, string label, string initial, Sprite icon, float x, float y, float w)
        {
            GameObject cell = CreateImageObject(prefix + "Intel", parent, new Color(0f, 0f, 0f, 0.24f), null);
            SetRect(cell.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, y), new Vector2(w, 56f));
            GameObject ic = CreateImageObject(prefix + "Icon", cell.transform, Toxic, icon);
            SetRect(ic.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(24f, 24f));
            SetMember(GetUiComponent(ic, imageType), "preserveAspect", true);
            GameObject lt = CreateTextObject(prefix + "Label", cell.transform, label, 9, FontStyle.Bold, Muted);
            SetRect(lt.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(8f, -4f), new Vector2(-16f, 15f));
            SetTextAlignment(lt, TextAnchor.MiddleRight);
            GameObject val = CreateTextObject(prefix + "Value", cell.transform, initial, prefix == "Rank" ? 12 : 17, FontStyle.Bold, White);
            SetRect(val.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(-6f, 5f), new Vector2(w - 40f, 29f));
            SetTextAlignment(val, TextAnchor.MiddleRight);
        }

        private static void CreateGearCell(Transform parent, string prefix, string label, Sprite icon, float x, float y, float w)
        {
            GameObject cell = CreateImageObject(prefix + "Gear", parent, new Color(0f, 0f, 0f, 0.24f), null);
            SetRect(cell.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, y), new Vector2(w, 52f));
            GameObject ic = CreateImageObject(prefix + "GearIcon", cell.transform, Toxic, icon);
            SetRect(ic.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(27f, 27f));
            SetMember(GetUiComponent(ic, imageType), "preserveAspect", true);
            GameObject lt = CreateTextObject(prefix + "GearLabel", cell.transform, label, 8, FontStyle.Bold, Muted);
            SetRect(lt.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(6f, -4f), new Vector2(-12f, 14f));
            SetTextAlignment(lt, TextAnchor.MiddleRight);
            GameObject q = CreateTextObject(prefix + "Quality", cell.transform, "--", 16, FontStyle.Bold, White);
            SetRect(q.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(-6f, 3f), new Vector2(w - 41f, 28f));
            SetTextAlignment(q, TextAnchor.MiddleRight);
        }

        private static void CreateLoadoutPanel(Transform parent)
        {
            GameObject panel = CreateImageObject("LoadoutPanel", parent, new Color32(7, 18, 10, 235), null);
            SetRect(panel.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(1392f, 52f), new Vector2(484f, 128f));
            AddOutline(panel, new Color(ToxicDim.r, ToxicDim.g, ToxicDim.b, 0.75f), new Vector2(1f, -1f));
            CreateWeaponRow(panel.transform, "Primary", "PRIMARY", LoadSprite("primary"), 6f, 66f, 472f);
            CreateWeaponRow(panel.transform, "Secondary", "SECONDARY", LoadSprite("secondary"), 6f, 7f, 472f);
        }

        private static void CreateWeaponRow(Transform parent, string prefix, string label, Sprite icon, float x, float y, float w)
        {
            GameObject row = CreateImageObject(prefix + "Row", parent, new Color(0f, 0f, 0f, 0.26f), null);
            SetRect(row.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, y), new Vector2(w, 55f));

            GameObject active = CreateImageObject(prefix + "Active", row.transform, new Color(Toxic.r, Toxic.g, Toxic.b, 0.12f), null);
            Stretch(active.GetComponent<RectTransform>(), 1f);
            AddOutline(active, Toxic, new Vector2(1f, -1f));
            active.SetActive(false);

            GameObject ic = CreateImageObject(prefix + "Icon", row.transform, Toxic, icon);
            SetRect(ic.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(30f, 30f));
            SetMember(GetUiComponent(ic, imageType), "preserveAspect", true);

            GameObject lt = CreateTextObject(prefix + "Label", row.transform, label, 8, FontStyle.Bold, Muted);
            SetRect(lt.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -4f), new Vector2(82f, 13f));
            SetTextAlignment(lt, TextAnchor.MiddleLeft);

            GameObject name = CreateTextObject(prefix + "Name", row.transform, "EMPTY", 13, FontStyle.Bold, White);
            SetRect(name.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(44f, -6f), new Vector2(238f, 28f));
            SetTextAlignment(name, TextAnchor.MiddleLeft);

            GameObject ammo = CreateTextObject(prefix + "Ammo", row.transform, "--", 13, FontStyle.Bold, Toxic);
            SetRect(ammo.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-144f, -6f), new Vector2(78f, 28f));
            SetTextAlignment(ammo, TextAnchor.MiddleRight);

            GameObject mode = CreateTextObject(prefix + "Mode", row.transform, "", 8, FontStyle.Bold, Muted);
            SetRect(mode.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-62f, -4f), new Vector2(54f, 13f));
            SetTextAlignment(mode, TextAnchor.MiddleRight);

            GameObject q = CreateTextObject(prefix + "Quality", row.transform, "--", 12, FontStyle.Bold, White);
            SetRect(q.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-8f, -6f), new Vector2(64f, 28f));
            SetTextAlignment(q, TextAnchor.MiddleRight);
        }

        private static void CreateHotkeyStrip(Transform parent)
        {
            GameObject strip = CreateImageObject("HotkeyStrip", parent, new Color32(7, 18, 10, 235), null);
            SetRect(strip.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(872f, 8f), new Vector2(1004f, 38f));
            AddOutline(strip, new Color(ToxicDim.r, ToxicDim.g, ToxicDim.b, 0.75f), new Vector2(1f, -1f));
            int[] keys = { 3, 4, 5, 6, 7, 8, 9, 0 };
            float cellW = 122f;
            for (int i = 0; i < keys.Length; i++)
                CreateHotkeyCell(strip.transform, keys[i], 5f + i * 124f, 4f, cellW);
        }

        private static void CreateHotkeyCell(Transform parent, int keyNumber, float x, float y, float w)
        {
            string p = "Hotkey" + keyNumber;
            GameObject cell = CreateImageObject(p + "Cell", parent, new Color(0f, 0f, 0f, 0.25f), null);
            SetRect(cell.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, y), new Vector2(w, 30f));
            GameObject key = CreateTextObject(p + "Key", cell.transform, keyNumber.ToString(), 11, FontStyle.Bold, Toxic);
            SetRect(key.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(18f, 20f));
            SetTextAlignment(key, TextAnchor.MiddleCenter);
            GameObject name = CreateTextObject(p + "Name", cell.transform, "--", 9, FontStyle.Bold, White);
            SetRect(name.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(27f, 0f), new Vector2(-50f, 20f));
            SetTextAlignment(name, TextAnchor.MiddleLeft);
            GameObject amount = CreateTextObject(p + "Amount", cell.transform, "", 9, FontStyle.Bold, Muted);
            SetRect(amount.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-5f, 0f), new Vector2(34f, 20f));
            SetTextAlignment(amount, TextAnchor.MiddleRight);
        }

        private static void CreateOxygenAlert(Transform root)
        {
            GameObject panel = CreateImageObject("OxygenPanel", root, new Color32(4, 12, 9, 238), null);
            SetRect(panel.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 238f), new Vector2(246f, 46f));
            AddOutline(panel, new Color32(99, 215, 255, 255), new Vector2(1f, -1f));
            GameObject icon = CreateImageObject("OxygenIcon", panel.transform, new Color32(99, 215, 255, 255), LoadSprite("oxygen"));
            SetRect(icon.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(30f, 30f));
            SetMember(GetUiComponent(icon, imageType), "preserveAspect", true);
            GameObject label = CreateTextObject("OxygenLabel", panel.transform, "OXYGEN", 9, FontStyle.Bold, Muted);
            SetRect(label.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -4f), new Vector2(86f, 14f));
            SetTextAlignment(label, TextAnchor.MiddleLeft);
            GameObject val = CreateTextObject("OxygenValue", panel.transform, "100", 19, FontStyle.Bold, White);
            SetRect(val.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-8f, -1f), new Vector2(70f, 28f));
            SetTextAlignment(val, TextAnchor.MiddleRight);
            panel.SetActive(false);
        }

        private static void CreateCornerBracket(Transform parent, bool left)
        {
            float x = left ? 2f : -2f;
            float anchor = left ? 0f : 1f;
            float pivot = left ? 0f : 1f;

            GameObject v = CreateImageObject(left ? "BracketLeftV" : "BracketRightV", parent, Toxic, null);
            SetRect(v.GetComponent<RectTransform>(), new Vector2(anchor, 0f), new Vector2(anchor, 0f), new Vector2(pivot, 0f),
                new Vector2(x, 2f), new Vector2(2f, 34f));

            GameObject h = CreateImageObject(left ? "BracketLeftH" : "BracketRightH", parent, Toxic, null);
            SetRect(h.GetComponent<RectTransform>(), new Vector2(anchor, 0f), new Vector2(anchor, 0f), new Vector2(pivot, 0f),
                new Vector2(x, 2f), new Vector2(34f, 2f));
        }

        private static GameObject CreateImageObject(string name, Transform parent, Color color, Sprite sprite)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Component image = go.AddComponent(imageType);
            SetMember(image, "color", color);
            SetMember(image, "sprite", sprite != null ? sprite : LoadSprite("solid"));
            SetMember(image, "raycastTarget", false);
            return go;
        }

        private static GameObject CreateTextObject(string name, Transform parent, string value, int fontSize, FontStyle style, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Component text = go.AddComponent(textType);

            SetMember(text, "text", value);
            SetMember(text, "font", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            SetMember(text, "fontSize", fontSize);
            SetMember(text, "fontStyle", style);
            SetMember(text, "color", color);
            SetMember(text, "raycastTarget", false);
            SetMember(text, "horizontalOverflow", HorizontalWrapMode.Overflow);
            SetMember(text, "verticalOverflow", VerticalWrapMode.Overflow);
            return go;
        }

        private static void SetTextAlignment(GameObject go, TextAnchor alignment)
        {
            SetMember(GetUiComponent(go, textType), "alignment", alignment);
        }

        private static Component GetUiComponent(GameObject go, Type type)
        {
            Component component = go.GetComponent(type);
            if (component == null)
                throw new Exception("Expected component is missing on " + go.name + ": " + type.FullName);
            return component;
        }

        private static void AddOutline(GameObject go, Color color, Vector2 distance)
        {
            Component outline = go.GetComponent(outlineType);
            if (outline == null)
                outline = go.AddComponent(outlineType);
            SetMember(outline, "effectColor", color);
            SetMember(outline, "effectDistance", distance);
            SetMember(outline, "useGraphicAlpha", true);
        }

        private static void SetMember(object instance, string memberName, object value)
        {
            if (instance == null)
                throw new ArgumentNullException("instance");

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, ConvertValue(value, property.PropertyType), null);
                return;
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(instance, ConvertValue(value, field.FieldType));
                return;
            }

            throw new MissingMemberException(type.FullName, memberName);
        }

        private static void SetEnumMember(object instance, string memberName, string enumName)
        {
            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
            {
                object value = Enum.Parse(property.PropertyType, enumName);
                property.SetValue(instance, value, null);
                return;
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                object value = Enum.Parse(field.FieldType, enumName);
                field.SetValue(instance, value);
                return;
            }

            throw new MissingMemberException(type.FullName, memberName);
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return null;

            Type valueType = value.GetType();
            if (targetType.IsAssignableFrom(valueType))
                return value;

            if (targetType.IsEnum && value is string)
                return Enum.Parse(targetType, (string)value);

            return Convert.ChangeType(value, targetType);
        }

        private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            rt.localScale = Vector3.one;
        }

        private static void AssignMasterBundle()
        {
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith(BundleRoot + "/", StringComparison.Ordinal))
                    continue;
                if (AssetDatabase.IsValidFolder(path))
                    continue;

                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer != null)
                    importer.SetAssetBundleNameAndVariant(BundleName, string.Empty);
            }

            AssetDatabase.RemoveUnusedAssetBundleNames();
            AssetDatabase.SaveAssets();
        }

        private static void WriteWorkshopDataFiles()
        {
            string outputRoot = GetWorkshopOutputRoot();
            string bundlesRoot = Path.Combine(outputRoot, "Bundles");
            string effectRoot = Path.Combine(bundlesRoot, "Effects", "PHPVEToxicGreenHUD");

            Directory.CreateDirectory(bundlesRoot);
            Directory.CreateDirectory(effectRoot);

            File.WriteAllText(Path.Combine(bundlesRoot, "MasterBundle.dat"),
@"Asset_Bundle_Name phpve_toxic_green_hud.masterbundle
Asset_Prefix Assets/PHPVEToxicGreenHUDMasterBundle
Asset_Bundle_Version 6
");

            File.WriteAllText(Path.Combine(effectRoot, "PHPVEToxicGreenHUD.dat"),
@"GUID f86ba8f388f04901b50f8b0fe90eebe2
Type Effect
ID 65041
Lifetime 0
Lifetime_Spread 0
Preload 0
");

            File.WriteAllText(Path.Combine(effectRoot, "English.dat"),
@"Name PH PVE Toxic Green Survival HUD
");

            File.WriteAllText(Path.Combine(outputRoot, "PLUGIN-CONFIG.xml"),
@"<?xml version=""1.0"" encoding=""utf-8""?>
<SurvivalHudConfiguration>
  <EffectGuid>f86ba8f3-88f0-4901-b50f-8b0fe90eebe2</EffectGuid>
  <EffectKey>32041</EffectKey>
  <HideVanillaLifeMeters>true</HideVanillaLifeMeters>
  <HideVanillaStatusIcons>true</HideVanillaStatusIcons>
  <HideVanillaGunStatus>true</HideVanillaGunStatus>
  <HideWhileDead>true</HideWhileDead>
  <DisplayVirusAsInfection>true</DisplayVirusAsInfection>
  <ShowOxygenOnlyWhenBelowFull>true</ShowOxygenOnlyWhenBelowFull>
  <CriticalHealthThreshold>25</CriticalHealthThreshold>
  <WarningFoodThreshold>25</WarningFoodThreshold>
  <WarningWaterThreshold>25</WarningWaterThreshold>
  <WarningInfectionThreshold>50</WarningInfectionThreshold>
  <InitialDisplayDelaySeconds>1.25</InitialDisplayDelaySeconds>
  <PollIntervalSeconds>0.25</PollIntervalSeconds>
  <RankPriorityCsv>founder,owner,staff,admin,moders,moder,elite,ceo,noble,member,default</RankPriorityCsv>
</SurvivalHudConfiguration>
");

            File.WriteAllText(Path.Combine(outputRoot, "WORKSHOP-README.txt"),
@"PH PVE TOXIC GREEN SURVIVAL HUD
================================

Effect GUID: f86ba8f3-88f0-4901-b50f-8b0fe90eebe2
Effect ID:   65041

UPLOAD TO STEAM WORKSHOP:
Upload the CONTENTS of this folder as an Unturned Workshop content mod.

SERVER:
1. Add the resulting Workshop File ID to WorkshopDownloadConfig.json.
2. Copy PLUGIN-CONFIG.xml to:
   Rocket/Plugins/PHPVESurvivalHUD/PHPVESurvivalHUD.configuration.xml
3. Restart the server cold.
4. Confirm the workshop asset loads before PHPVESurvivalHUD hides vanilla life meters.

IMPORTANT:
Keep the filename exactly 'MasterBundle.dat' for Linux compatibility.
Do not delete the .masterbundle.hash file from release builds.
");
        }

        private static void ValidatePrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                throw new Exception("HUD prefab was not generated: " + PrefabPath);

            List<string> required = new List<string>
            {
                "HealthValue", "FoodValue", "WaterValue", "InfectionValue", "StaminaValue",
                "OxygenValue", "OxygenPanel", "BleedingValue", "BrokenValue",
                "TemperatureValueNormal", "TemperatureValueWarm", "TemperatureValueCold",
                "HealthCritical", "FoodWarning", "WaterWarning", "InfectionWarning", "BleedingWarning", "BrokenWarning",
                "ECGNormal", "ECGFast", "ECGCritical",
                "XPValue", "KillsValue", "DeathsValue", "RankValue",
                "HelmetQuality", "VestQuality", "TopQuality", "BottomQuality",
                "PrimaryName", "PrimaryAmmo", "PrimaryQuality", "PrimaryMode", "PrimaryActive",
                "SecondaryName", "SecondaryAmmo", "SecondaryQuality", "SecondaryMode", "SecondaryActive"
            };

            string[] prefixes = { "Health", "Food", "Water", "Infection", "Stamina" };
            foreach (string prefix in prefixes)
                for (int i = 1; i <= 10; i++)
                    required.Add(prefix + "Seg" + i);

            int[] hotkeys = { 3, 4, 5, 6, 7, 8, 9, 0 };
            foreach (int key in hotkeys)
            {
                required.Add("Hotkey" + key + "Name");
                required.Add("Hotkey" + key + "Amount");
            }

            foreach (string name in required)
            {
                if (FindRecursive(prefab.transform, name) == null)
                    throw new Exception("Required UI object is missing: " + name);
            }

            Debug.Log("[PH PVE Toxic Green HUD] Contract validation passed (" + required.Count + " named UI objects).");
        }

        private static Transform FindRecursive(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindRecursive(parent.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static void ValidateBuildOutput(string outputRoot)
        {
            string bundle = Path.Combine(outputRoot, "Bundles", BundleName);
            if (!File.Exists(bundle))
                throw new Exception("Windows/current-platform master bundle missing: " + bundle);

            string hash = bundle + ".hash";
            if (!File.Exists(hash))
                throw new Exception("Release integrity hash missing. Ensure multiplatform build succeeded: " + hash);

            Debug.Log("[PH PVE Toxic Green HUD] Bundle and integrity hash verified.");
        }
    }
}
#endif
