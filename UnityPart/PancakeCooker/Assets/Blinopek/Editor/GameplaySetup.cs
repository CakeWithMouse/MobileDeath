using System;
using System.IO;
using Blinopek.Bootstrap;
using Blinopek.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blinopek.Editor
{
    public static class GameplaySetup
    {
        public const string ContentPath = "Assets/Blinopek/Content/Gameplay.asset";
        public const string BootstrapPath = "Assets/Blinopek/Scenes/Bootstrap.unity";
        public const string CafePath = "Assets/Blinopek/Scenes/Cafe.unity";

        [MenuItem("Blinopek/Prepare gameplay scenes")]
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/Blinopek/Content"); Directory.CreateDirectory("Assets/Blinopek/Scenes"); AssetDatabase.Refresh();
            var config = AssetDatabase.LoadAssetAtPath<GameContent>(ContentPath);
            if (config == null) { config = ScriptableObject.CreateInstance<GameContent>(); AssetDatabase.CreateAsset(config, ContentPath); }
            config.bodyFont = Load<Font>("ui/fonts/PT_Sans-Web-Regular.ttf"); config.titleFont = Load<Font>("ui/fonts/PT_Serif-Web-Regular.ttf");
            config.signboardFont = Load<Font>("ui/fonts/YesevaOne-Regular.ttf");
            config.signboardShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Blinopek/Presentation/Signboard.shader");
            config.kitchenBackground = Load<Texture2D>("kitchen/kitchen-background.png");
            config.stove = Load<Texture2D>("kitchen/stove.png"); config.assemblyTable = Load<Texture2D>("kitchen/assembly-table.png");
            config.counterBackground = Load<Texture2D>("decor/service_background.png"); config.counter = Load<Texture2D>("decor/service_counter.png");
            config.pan = Load<Texture2D>("kitchen/pan-base.png"); config.handle = Load<Texture2D>("kitchen/pan-handle.png");
            config.plate = Load<Texture2D>("kitchen/plate.png"); config.trash = Load<Texture2D>("kitchen/discard-bowl.png"); config.tray = Load<Texture2D>("kitchen/ready-tray.png");
            config.raw = Load<Texture2D>("kitchen/batter-raw.png"); config.ready = Load<Texture2D>("kitchen/pancake-ready.png"); config.burnt = Load<Texture2D>("kitchen/pancake-burnt.png");
            string[] ingredients = { "butter", "jam", "honey", "curd", "mushrooms" };
            for (int i = 0; i < 5; i++) { config.fillings[i] = Load<Texture2D>("kitchen/filling-" + ingredients[i] + ".png"); config.containers[i] = Load<Texture2D>("kitchen/container-" + ingredients[i] + ".png"); }
            string[] people = { "student", "office", "craftswoman", "nurse", "elder-woman", "elder-man" };
            for (int i = 0; i < 6; i++) {
                config.visitors[i] = Load<Texture2D>("visitors/" + people[i] + "-neutral.png");
                config.positiveVisitors[i] = Load<Texture2D>("visitors/" + people[i] + "-positive.png");
                config.negativeVisitors[i] = Load<Texture2D>("visitors/" + people[i] + "-negative.png");
            }
            config.pancakeShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Blinopek/Presentation/Pancake.shader");
            FigmaArtworkSetup.Populate(config);
            AudioSetup.Populate(config);
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssets();
            if (!File.Exists(BootstrapPath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, UnityEngine.Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
                var go = new GameObject("AppRoot"); SceneManager.MoveGameObjectToScene(go, scene); go.AddComponent<AppBootstrap>().content = config;
                EditorSceneManager.SaveScene(scene, BootstrapPath); if (!UnityEngine.Application.isBatchMode) EditorSceneManager.CloseScene(scene, true);
            }
            if (!File.Exists(CafePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, UnityEngine.Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
                var go = new GameObject("CafeCamera"); SceneManager.MoveGameObjectToScene(go, scene); var camera = go.AddComponent<Camera>();
                camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .16f, .12f); go.tag = "MainCamera";
                EditorSceneManager.SaveScene(scene, CafePath); if (!UnityEngine.Application.isBatchMode) EditorSceneManager.CloseScene(scene, true);
            }
            var existing = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            existing.Add(new EditorBuildSettingsScene(BootstrapPath, true)); existing.Add(new EditorBuildSettingsScene(CafePath, true));
            foreach (var scene in EditorBuildSettings.scenes) if (scene.path != BootstrapPath && scene.path != CafePath) existing.Add(scene);
            EditorBuildSettings.scenes = existing.ToArray();
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath);
            Debug.Log("Blinopek core prepared. Press Play to start Bootstrap → Cafe. Development save is separate from server data.");
        }
        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            string full = "Assets/Art/Blinopek/" + path; var value = AssetDatabase.LoadAssetAtPath<T>(full);
            if (value == null) throw new FileNotFoundException("Missing gameplay asset: " + full); return value;
        }
        [MenuItem("Blinopek/Play core")]
        public static void PlayCore() { Prepare(); EditorApplication.isPlaying = true; }
        [MenuItem("Blinopek/Build Windows player")]
        public static void BuildPlayer() { Build(false); }

        [MenuItem("Blinopek/Build Windows QA player")]
        public static void BuildValidationPlayer() { Build(true); }

        private static void Build(bool includeSmokeTest)
        {
            Prepare(); Directory.CreateDirectory("Builds/Core");
            // QA uses the same player options as the playable build. Only the opt-in driver differs.
            var options = new BuildPlayerOptions {
                scenes = new[] { BootstrapPath, CafePath }, locationPathName = "Builds/Core/Blinopek.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
                extraScriptingDefines = includeSmokeTest ? new[] { "BLINOPEK_QA" } : new string[0]
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Core player build failed.");
        }
    }
}
