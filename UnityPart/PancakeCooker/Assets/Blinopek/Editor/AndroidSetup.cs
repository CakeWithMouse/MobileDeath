using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Blinopek.Editor
{
    public static class AndroidSetup
    {
        [MenuItem("Blinopek/Build Android APK")]
        public static void BuildPlayer()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Install Android Build Support, Android SDK & NDK Tools and OpenJDK for this Unity editor.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Switch the active platform to Android first. For batch mode, pass -buildTarget Android.");

            GameplaySetup.Prepare();
            PlayerSettings.productName = "Блинопёк";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.blinopek.game");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.buildApkPerCpuArchitecture = false;
            PlayerSettings.Android.splitApplicationBinary = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ETC2;

            // Local installation build. Publishing requires a separately configured release key.
            string output = Environment.GetEnvironmentVariable("BLINOPEK_ANDROID_APK");
            if (string.IsNullOrEmpty(output)) output = "Builds/Android/Blinopek.apk";
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            AssetDatabase.SaveAssets();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { GameplaySetup.BootstrapPath, GameplaySetup.CafePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Android APK build failed: " + report.summary.totalErrors + " errors.");

            string directory = Path.GetDirectoryName(output);
            File.Copy("Assets/Audio/Blinopek/CREDITS.txt", Path.Combine(directory, "AUDIO-CREDITS.txt"), true);
            File.Copy("Assets/Audio/Blinopek/audio-manifest.json", Path.Combine(directory, "audio-manifest.json"), true);
            File.WriteAllText(Path.Combine(directory, "build-summary.txt"),
                "Result: " + report.summary.result + "\n" +
                "Unity: " + UnityEngine.Application.unityVersion + "\n" +
                "Package: " + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) + "\n" +
                "Version: " + PlayerSettings.bundleVersion + " (" + PlayerSettings.Android.bundleVersionCode + ")\n" +
                "Architecture: ARM64 (IL2CPP)\nMinimum Android API: 26\nSigning: Android debug key\n" +
                "Bytes: " + report.summary.totalSize + "\n" +
                "Errors: " + report.summary.totalErrors + "\n" +
                "Warnings: " + report.summary.totalWarnings + "\n" +
                "Duration: " + report.summary.totalTime + "\n");
            Debug.Log("BLINOPEK_ANDROID_BUILD_SUCCEEDED: " + output);
        }
    }
}
