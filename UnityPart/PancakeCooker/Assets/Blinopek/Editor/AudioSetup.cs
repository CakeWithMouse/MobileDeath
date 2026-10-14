using System;
using System.IO;
using System.Linq;
using Blinopek.Application;
using Blinopek.Presentation;
using UnityEditor;
using UnityEngine;

namespace Blinopek.Editor
{
    public static class AudioSetup
    {
        private const string Root = "Assets/Audio/Blinopek/";
        [InitializeOnLoadMethod]
        private static void ScheduleConnection() { if (!UnityEngine.Application.isBatchMode) EditorApplication.delayCall += ConnectNewLibrary; }
        private static void ConnectNewLibrary()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += ConnectNewLibrary; return; }
            var content = AssetDatabase.LoadAssetAtPath<GameContent>(GameplaySetup.ContentPath);
            if (content == null || content.audio != null || !File.Exists(Root + "audio-manifest.json")) return;
            Populate(content); EditorUtility.SetDirty(content); AssetDatabase.SaveAssets();
        }
        public static void Populate(GameContent content)
        {
            var files = Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".wav") || p.EndsWith(".ogg")).Select(p => p.Replace('\\', '/')).ToArray();
            if (files.Length == 0) throw new FileNotFoundException("No prepared game audio in " + Root);
            foreach (string path in files)
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                bool music = path.Contains("/Music/"), effect = path.Contains("/Sfx/");
                var settings = importer.defaultSampleSettings;
                settings.loadType = music ? AudioClipLoadType.Streaming : effect ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = effect ? AudioCompressionFormat.PCM : AudioCompressionFormat.Vorbis;
                settings.quality = .8f; settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData = !music;
                // Effects/ambience are already mono. Avoid Unity's downmix normalization changing their prepared level.
                bool changed = !importer.defaultSampleSettings.Equals(settings) || importer.forceToMono || importer.loadInBackground != music;
                if (changed) { importer.defaultSampleSettings = settings; importer.forceToMono = false; importer.loadInBackground = music; importer.SaveAndReimport(); }
            }
            const string asset = "Assets/Blinopek/Content/Audio.asset";
            var library = AssetDatabase.LoadAssetAtPath<GameAudioLibrary>(asset);
            if (library == null) { library = ScriptableObject.CreateInstance<GameAudioLibrary>(); AssetDatabase.CreateAsset(library, asset); }
            library.sounds = Enum.GetValues(typeof(SoundCue)).Cast<SoundCue>().Select(cue => {
                var clips = files.Where(p => Path.GetFileName(p).StartsWith(cue + "__", StringComparison.Ordinal))
                    .OrderBy(p => p).Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p)).ToArray();
                if (clips.Length == 0 || clips.Any(c => c == null)) throw new Exception("Missing sound: " + cue);
                return new SoundEntry { cue = cue, clips = clips, gain = cue == SoundCue.Patience || cue == SoundCue.Ready ? .4f : .65f };
            }).ToArray();
            library.frying = Clip("Loops/frying.ogg"); library.batter = Clip("Loops/batter.ogg");
            library.butter = Clip("Loops/butter.ogg"); library.jam = Clip("Loops/jam.ogg");
            library.honey = Clip("Loops/honey.ogg"); library.cafe = Clip("Loops/cafe.ogg");
            library.musicDay = Clip("Music/day.ogg"); library.musicMenu = Clip("Music/menu.ogg");
            content.audio = library; EditorUtility.SetDirty(library);
        }
        private static AudioClip Clip(string path)
        { var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + path); if (clip == null) throw new FileNotFoundException(path); return clip; }

        public static void BuildValidationPlayer() { Build(true); }
        public static void BuildPlayer() { Build(false); }
        private static void Build(bool qa)
        {
            GameplaySetup.Prepare();
            string directory = qa ? "Builds/AudioValidation" : "Builds/Audio"; Directory.CreateDirectory(directory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { GameplaySetup.BootstrapPath, GameplaySetup.CafePath },
                locationPathName = directory + "/Blinopek.exe", target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None, extraScriptingDefines = qa ? new[] { "BLINOPEK_QA" } : new string[0]
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Audio player build failed.");
            File.Copy(Root + "CREDITS.txt", directory + "/AUDIO-CREDITS.txt", true);
            File.Copy(Root + "audio-manifest.json", directory + "/audio-manifest.json", true);
        }
    }
}
