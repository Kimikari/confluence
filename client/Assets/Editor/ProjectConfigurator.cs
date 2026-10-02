using System;
using System.IO;
using System.Reflection;
using Confluence.Client.App;
using Confluence.Client.Arena;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Confluence.Client.EditorTools
{
    /// <summary>
    /// Idempotent project setup, so every setting the client depends on is reproducible from code:
    /// company/product name, linear color space, .NET Standard 2.1 API level, Mono backend, Input System as the
    /// only active input handling, the URP pipeline and renderer assets (assigned in Graphics and every Quality
    /// level), the three base materials the runtime copies, and the bootstrap scene in the build list.
    /// Run from the menu (Confluence > Configure Project) or in batch mode:
    /// Unity.exe -batchmode -quit -projectPath client -executeMethod Confluence.Client.EditorTools.ProjectConfigurator.Configure
    /// </summary>
    public static class ProjectConfigurator
    {
        public const string SettingsFolder = "Assets/Settings";
        public const string RendererPath = SettingsFolder + "/ConfluenceUniversalRenderer.asset";
        public const string PipelinePath = SettingsFolder + "/ConfluenceUniversalRP.asset";
        public const string MaterialsFolder = "Assets/Resources/Materials";
        public const string ScenePath = "Assets/Scenes/Bootstrap.unity";
        public const string ProductName = "Confluence";
        public const string Version = "0.1.0";

        // ProjectSettings.asset field: 0 = Input Manager (old), 1 = Input System package (new), 2 = both.
        private const int InputSystemOnly = 1;

        [MenuItem("Confluence/Configure Project")]
        public static void Configure()
        {
            ConfigurePlayer();
            bool inputChanged = ConfigureInputHandling();
            UniversalRenderPipelineAsset pipeline = EnsurePipeline();
            AssignPipeline(pipeline);
            EnsureGlobalSettings();
            EnsureBaseMaterials();
            EnsureBootstrapScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Configure] Project configured" + (inputChanged ? "; active input handling changed, restart the editor before building." : "."));
        }

        /// <summary>True when the project is ready for a build (used by BuildScript before building).</summary>
        public static bool Verify(out string problem)
        {
            if (ActiveInputHandler() != InputSystemOnly)
            {
                problem = "Active input handling is not 'Input System Package (New)'. Run ProjectConfigurator.Configure and restart.";
                return false;
            }

            if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset))
            {
                problem = "URP is not the default render pipeline.";
                return false;
            }

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                problem = "Color space is not linear.";
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                problem = "Missing " + ScenePath + ".";
                return false;
            }

            problem = null;
            return true;
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = ProductName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            NamedBuildTarget standalone = NamedBuildTarget.Standalone;
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.SplashScreen.show = false;
        }

        private static int ActiveInputHandler()
        {
            var so = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            SerializedProperty property = so.FindProperty("activeInputHandler");
            return property == null ? -1 : property.intValue;
        }

        // There is no public API for this setting; the Input System package itself edits the serialized field.
        private static bool ConfigureInputHandling()
        {
            var so = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            SerializedProperty property = so.FindProperty("activeInputHandler");
            if (property == null)
            {
                throw new InvalidOperationException("PlayerSettings has no 'activeInputHandler' property in this Unity version.");
            }

            if (property.intValue == InputSystemOnly)
            {
                return false;
            }

            property.intValue = InputSystemOnly;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static UniversalRenderPipelineAsset EnsurePipeline()
        {
            EnsureFolder(SettingsFolder);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            // One competitive-quality preset (spec L421: 120 fps target on the reference PC).
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            pipeline.mainLightShadowmapResolution = 2048;
            pipeline.shadowDistance = 90f;
            pipeline.shadowCascadeCount = 4;
            EditorUtility.SetDirty(pipeline);
            EditorUtility.SetDirty(renderer);
            return pipeline;
        }

        private static void AssignPipeline(UniversalRenderPipelineAsset pipeline)
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }

            QualitySettings.SetQualityLevel(current, false);
        }

        // URP creates its global settings asset on demand (and requires it to build); create it now so it is
        // committed. The entry point is internal, hence reflection; failure only means the build creates it.
        private static void EnsureGlobalSettings()
        {
            Type type = typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
            MethodInfo ensure = type?.GetMethod("Ensure", BindingFlags.NonPublic | BindingFlags.Static);
            if (ensure == null)
            {
                Debug.LogWarning("[Configure] UniversalRenderPipelineGlobalSettings.Ensure not found; URP will create its global settings on first build.");
                return;
            }

            ParameterInfo[] parameters = ensure.GetParameters();
            object[] args = parameters.Length == 0 ? Array.Empty<object>() : new object[] { true };
            ensure.Invoke(null, args);
        }

        private static void EnsureBaseMaterials()
        {
            EnsureFolder(MaterialsFolder);
            EnsureMaterial(ArenaMaterialLibrary.OpaqueResource, "Universal Render Pipeline/Lit", m => { });
            EnsureMaterial(ArenaMaterialLibrary.TransparentResource, "Universal Render Pipeline/Lit", m =>
            {
                MaterialSetup.MakeTransparent(m, false);
                UnityEditor.BaseShaderGUI.SetupMaterialBlendMode(m);
            });
            EnsureMaterial(ArenaMaterialLibrary.UnlitResource, "Universal Render Pipeline/Unlit", m =>
            {
                MaterialSetup.MakeTransparent(m, true);
                UnityEditor.BaseShaderGUI.SetupMaterialBlendMode(m);
            });
        }

        private static void EnsureMaterial(string resourceName, string shaderName, Action<Material> setup)
        {
            string path = "Assets/Resources/" + resourceName + ".mat";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException("Shader '" + shaderName + "' not found; is URP installed?");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                setup(material);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
                setup(material);
                EditorUtility.SetDirty(material);
            }
        }

        private static void EnsureBootstrapScene()
        {
            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = new GameObject("Bootstrap");
                go.AddComponent<Bootstrap>();
                RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
                RenderSettings.fog = false;
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
