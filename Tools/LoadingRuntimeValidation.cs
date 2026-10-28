// Copy with LoadingValidation.cs and MenuNavigationValidation.cs into Editor.
// Create %TEMP%/blinopek-runtime-result/run.flag and refresh Unity to run with an isolated save.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Blinopek.Bootstrap;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class LoadingRuntimeValidation
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "blinopek-runtime-result");
    private static string Flag => Path.Combine(Folder, "run.flag");
    private struct ProgressSample { public float Time, Actual, Display; }
    private static readonly List<ProgressSample> samples = new List<ProgressSample>();
    private static double traceShownAt = -1;
    private static int traceFrame = -1;
    private const float MinimumSeconds = 1f;
    private static int step;
    private static double shownAt = -1;
    private static bool awaitingHide;
    private static double longStarted;
    private static double deadline, next;
    private static GameView view;
    private static LoadingScreen loading;
    private static DaySession session;
    private static bool sawLoading;
    private static int loadingFrames;
    private static float startupProgress;
    private static bool loadingCaptured;
    static LoadingRuntimeValidation()
    {
        if (File.Exists(Flag)) EditorApplication.delayCall += Begin;
        else if (SessionState.GetBool("Blinopek.RuntimeProbe", false)) { EditorApplication.update += Tick; deadline = EditorApplication.timeSinceStartup + 120; }
    }
    private static void Begin()
    {
        File.Delete(Flag);
        try
        {
            LoadingValidation.Run();
            File.WriteAllText(Path.Combine(Folder, "edit-passed.txt"), "LoadingValidation and MenuNavigationValidation completed");
            SessionState.SetString("Blinopek.PreviousStartScene", EditorSceneManager.playModeStartScene == null ? "" : AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Blinopek/Scenes/Bootstrap.unity");
            SessionState.SetFloat("Blinopek.PreviousTimeScale", Time.timeScale);
            shownAt = -1; awaitingHide = false;
            traceShownAt = -1; traceFrame = -1; samples.Clear();
            File.WriteAllText(Path.Combine(Folder, "progress.csv"), "step;frame;seconds;actual;display;ready\n");
            step = 0; sawLoading = false; loadingFrames = 0; startupProgress = 0; loadingCaptured = false;
            foreach (var name in new[] { "FAILED.txt", "PASSED.txt", "checks.txt" })
            { var file = Path.Combine(Folder, name); if (File.Exists(file)) File.Delete(file); }
            AppBootstrap.EditorValidationSavePath = Path.Combine(Folder, "runtime-" + Guid.NewGuid().ToString("N") + ".json");
            EditorApplication.update -= Tick; EditorApplication.update += Tick; deadline = EditorApplication.timeSinceStartup + 120;
            SessionState.SetBool("Blinopek.RuntimeProbe", true);
            EditorApplication.isPlaying = true;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Folder, "FAILED.txt"), ex.ToString()); Debug.LogException(ex); }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void IsolateSave()
    {
        if (!SessionState.GetBool("Blinopek.RuntimeProbe", false)) return;
        AppBootstrap.EditorValidationSavePath = Path.Combine(Folder, "runtime-" + Guid.NewGuid().ToString("N") + ".json");
        UnityEngine.Application.runInBackground = true;
        Time.timeScale = 0;
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        File.AppendAllText(Path.Combine(Folder, "checks.txt"), "PASS " + message + "\n");
    }
    private static void Click(string name)
    {
        var button = view.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
        Check(button.gameObject.activeInHierarchy && button.IsInteractable(), name + " available");
        button.onClick.Invoke(); next = EditorApplication.timeSinceStartup + .2;
    }
    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Runtime validation timeout at step " + step);
            loading = UnityEngine.Object.FindAnyObjectByType<LoadingScreen>();
            if (loading != null && loading.Visible)
            {
                SampleProgress();
                sawLoading = true; loadingFrames++;
                shownAt = loading.ShownAt; awaitingHide = true;
                if (step == 0)
                {
                    float progress = (float)typeof(LoadingScreen).GetField("progress", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loading);
                    if (progress < startupProgress) throw new Exception("Startup progress moved backwards");
                    startupProgress = progress;
                    if (!loadingCaptured && progress > .63f && progress < .9f)
                    { ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "loading.png")); loadingCaptured = true; }
                }
            }
            if (loading != null && !loading.Visible && awaitingHide)
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - shownAt;
                Check(elapsed >= MinimumSeconds, "Visible duration >= 1 realtime second at step " + step + ": " + elapsed.ToString("F3"));
                awaitingHide = false;
            }
            if (EditorApplication.timeSinceStartup < next) return;
            if (step == 0)
            {
                view = UnityEngine.Object.FindAnyObjectByType<GameView>();
                if (view == null || !view.enabled || loading == null || loading.Visible) return;
                Check(sawLoading && loadingFrames > 5, "Cold bootstrap displayed loading across multiple frames");
                Check(view.Session == null && view.Controller.Paused, "Cold bootstrap reached fresh main menu");
                ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "menu.png"));
                Click("ContinueShift"); Check(loading.Visible && !view.enabled, "Start blocks input and simulation"); step = 1;
            }
            else if (loading.Visible && step != 5 && step != 7) return;
            else if (step == 1)
            {
                Check(view.Session != null, "Shift started"); session = view.Session;
                view.Controller.SetPause(PauseReason.Menu, true); view.Refresh();
                Click("ExitToMainMenu"); step = 2;
            }
            else if (step == 2)
            {
                Check(view.Session == session && view.Controller.Paused, "Return to menu preserves paused shift");
                Click("ContinueShift"); step = 3;
            }
            else if (step == 3)
            {
                Check(view.Session == session && (view.Controller.Pauses & PauseReason.Menu) == 0, "Continue resumes same shift");
                view.Controller.SetPause(PauseReason.Menu, true); view.Refresh();
                Click("RestartDay"); step = 4;
            }
            else if (step == 4)
            {
                Check(view.Session != session && view.Session.StartProfile.day == session.StartProfile.day, "Restart replaces session on same day");
                ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "shift.png"));
                var bootstrap = UnityEngine.Object.FindAnyObjectByType<AppBootstrap>();
                var begin = typeof(AppBootstrap).GetMethod("BeginTransition", BindingFlags.Instance | BindingFlags.NonPublic);
                begin.Invoke(bootstrap, new object[] { (Action)(CreateRetryOperation()), "Проверка ошибки" });
                step = 5;
            }
            else if (step == 8)
            {
                Check(view.enabled && !loading.Visible, "Return after error restores input after minimum duration");
                var bootstrap = UnityEngine.Object.FindAnyObjectByType<AppBootstrap>();
                loading.Show("Проверка долгой операции"); view.SetLoading(true);
                longStarted = Time.realtimeSinceStartupAsDouble;
                var work = (IEnumerator)typeof(AppBootstrap).GetMethod("Guard", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(bootstrap, new object[] { LongWork(), (Action)(() => { }), true });
                bootstrap.StartCoroutine(work); step = 9;
            }
            else if (step == 9)
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - longStarted;
                Check(elapsed >= 2.4 && elapsed < 3.2, "Long operation has no extra 1 second tail: " + elapsed.ToString("F3"));
                Check(Time.timeScale == 0, "Timing checks completed at timeScale zero");
                Check(samples.Exists(sample => sample.Time > 1.05f && sample.Time < 1.2f && sample.Actual < .11f && sample.Display <= .101f), "Long operation at 10 percent does not drift towards 99 after the minimum");
                Check(samples.Exists(sample => sample.Time > 1.6f && sample.Time < 1.8f && sample.Actual == .45f && sample.Display > .3f && sample.Display < .46f), "Long displayed progress follows the real 45 percent report");
                Check(samples.Exists(sample => sample.Time > 2.1f && sample.Time < 2.35f && sample.Actual == .8f && sample.Display > .65f && sample.Display < .81f), "Long displayed progress follows the real 80 percent report");
                Check(samples.Exists(sample => sample.Time > MinimumSeconds && sample.Actual < 1 && sample.Display < 1), "Long operation stays below 100 after minimum until actual completion");
                var bootstrap = UnityEngine.Object.FindAnyObjectByType<AppBootstrap>();
                loading.Show("Проверка быстрой операции"); view.SetLoading(true);
                longStarted = Time.realtimeSinceStartupAsDouble;
                var work = (IEnumerator)typeof(AppBootstrap).GetMethod("Guard", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(bootstrap, new object[] { FastWork(), (Action)(() => { }), true });
                bootstrap.StartCoroutine(work); step = 10;
            }
            else if (step == 10)
            {
                Check(samples.Exists(sample => sample.Time < .5f && sample.Actual == 1 && sample.Display < .5f), "Fast actual completion does not complete the visible bar early");
                Check(samples.Exists(sample => sample.Time > .85f && sample.Time < MinimumSeconds && sample.Display > .8f && sample.Display < 1), "Fast visible progress keeps advancing near the end of the minimum");
                Check(samples.Count > 10, "Fast progress sampled on real Unity frames: " + samples.Count);
                int advances = 0; float largestStep = 0;
                for (int i = 1; i < samples.Count; i++) { float delta = samples[i].Display - samples[i - 1].Display; if (delta > .00001f) advances++; largestStep = Mathf.Max(largestStep, delta); }
                Check(advances > 10 && largestStep < .15f, "Fast progress is smooth across frames; advances=" + advances + " maxStep=" + largestStep.ToString("F4"));
                Check(samples.Exists(sample => sample.Display == 1 && sample.Time >= MinimumSeconds), "100 percent appears on the final ready frame");
                File.WriteAllText(Path.Combine(Folder, "PASSED.txt"), "Editor PlayMode lifecycle, timing and per-frame displayed progress checks passed");
                Finish();
            }
            else if (step == 6)
            {
                Check(view.enabled && !loading.Visible, "Retry completed and restored input");
                var bootstrap = UnityEngine.Object.FindAnyObjectByType<AppBootstrap>();
                typeof(AppBootstrap).GetMethod("BeginTransition", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bootstrap,
                    new object[] { (Action)(() => throw new InvalidOperationException("Injected validation error")), "Проверка возврата" });
                step = 7;
            }
            if (step == 5 && loading.Visible && !view.enabled)
            {
                var error = typeof(LoadingScreen).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loading) as string;
                if (error == null) return;
                var retry = (Action)typeof(LoadingScreen).GetField("retry", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loading);
                Check(retry != null, "Failed transition offers retry"); retry(); step = 6; next = EditorApplication.timeSinceStartup + .2;
            }
            if (step == 7 && loading.Visible && !view.enabled)
            {
                var error = typeof(LoadingScreen).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loading) as string;
                if (error == null) return;
                Check(error.Contains("Injected validation error"), "Transition error retained on loading screen");
                var back = (Action)typeof(LoadingScreen).GetField("back", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loading);
                Check(back != null, "Failed transition offers return"); back();
                step = 8;
            }
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Folder, "FAILED.txt"), ex.ToString()); Debug.LogException(ex); Finish(); }
    }
    private static void SampleProgress()
    {
        if (traceShownAt != loading.ShownAt) { traceShownAt = loading.ShownAt; traceFrame = -1; samples.Clear(); }
        if (traceFrame == Time.frameCount) return;
        traceFrame = Time.frameCount;
        float elapsed = (float)(Time.realtimeSinceStartupAsDouble - loading.ShownAt);
        float display = loading.DisplayedProgress;
        if (samples.Count > 0 && display < samples[samples.Count - 1].Display) throw new Exception("Displayed progress moved backwards");
        if (display >= 1 && (!loading.ReadyToClose || elapsed < MinimumSeconds || loading.ActualProgress < 1)) throw new Exception("Early 100 percent");
        if (!loading.ReadyToClose && loading.DisplayedPercent >= 100) throw new Exception("Rounded label reached 100 early");
        samples.Add(new ProgressSample { Time = elapsed, Actual = loading.ActualProgress, Display = display });
        File.AppendAllText(Path.Combine(Folder, "progress.csv"), string.Format(CultureInfo.InvariantCulture,
            "{0};{1};{2:F4};{3:F5};{4:F5};{5}\n", step, Time.frameCount, elapsed, loading.ActualProgress, display, loading.ReadyToClose));
    }
    private static IEnumerator LongWork()
    {
        loading.Report("Долгая работа", .1f);
        yield return new WaitForSecondsRealtime(1.2f);
        loading.Report("Долгая работа — следующий этап", .45f);
        yield return new WaitForSecondsRealtime(.6f);
        loading.Report("Долгая работа — завершающий этап", .8f);
        yield return new WaitForSecondsRealtime(.6f);
        loading.Report("Работа завершена", 1);
    }
    private static IEnumerator FastWork()
    {
        loading.Report("Быстрая работа", .1f);
        yield return new WaitForSecondsRealtime(.2f);
        loading.Report("Работа завершена", 1);
    }
    private static Action CreateRetryOperation()
    {
        int attempts = 0;
        return () => { if (attempts++ == 0) throw new InvalidOperationException("Injected validation error"); };
    }
    private static void Finish()
    {
        EditorApplication.update -= Tick;
        Time.timeScale = SessionState.GetFloat("Blinopek.PreviousTimeScale", 1);
        SessionState.SetBool("Blinopek.RuntimeProbe", false);
        AppBootstrap.EditorValidationSavePath = null;
        EditorApplication.isPlaying = false;
        var path = SessionState.GetString("Blinopek.PreviousStartScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
    }
}

