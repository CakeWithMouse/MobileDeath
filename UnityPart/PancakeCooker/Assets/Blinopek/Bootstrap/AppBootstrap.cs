using System;
using System.Collections;
using System.IO;
using Blinopek.Application;
using Blinopek.Infrastructure;
using Blinopek.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blinopek.Bootstrap
{
    public sealed class AppBootstrap : MonoBehaviour
    {
        // Retained for editor/tool compatibility; production Bootstrap has no heavy scene reference.
        public GameContent content;
#if UNITY_EDITOR
        // Editor validation can exercise the real bootstrap without touching player progress.
        public static string EditorValidationSavePath;
#endif
        private LoadingScreen loading;
        private LoadingConfig config;
        private GameView view;
        private bool busy;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || BLINOPEK_QA
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureSmokeRun()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-blinopekSmoke") >= 0)
                UnityEngine.Application.runInBackground = true;
        }
#endif
        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            UnityEngine.Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            loading = gameObject.AddComponent<LoadingScreen>();
            BeginStartup();
        }
        private void BeginStartup()
        {
            if (busy) return;
            if (view != null) { Destroy(view.gameObject); view = null; }
            busy = true; loading.Show("Открываем блинную");
            loading.SetMinimumVisibleTime(config?.minimumVisibleSeconds ?? 1f);
            StartCoroutine(Guard(Startup(), BeginStartup));
        }
        private IEnumerator Startup()
        {
            // Let the resource-free screen render before any file, scene or content work.
            yield return null; yield return null;
            var request = Resources.LoadAsync<TextAsset>("Loading/loading");
            yield return request;
            var json = request.asset as TextAsset;
            if (json == null) throw new InvalidOperationException("Не найден Loading/loading.json.");
            config = JsonUtility.FromJson<LoadingConfig>(json.text);
            if (config == null) throw new InvalidOperationException("Пустой конфиг загрузки.");
            config.Validate();
            loading.SetMinimumVisibleTime(config.minimumVisibleSeconds);
            loading.Report("Загружаем ресурсы кафе", .08f);
            var catalogRequest = Resources.LoadAsync<LoadingCatalog>("Loading/catalog");
            while (!catalogRequest.isDone) { loading.Report("Загружаем ресурсы кафе", .08f + catalogRequest.progress * .22f); yield return null; }
            var catalog = catalogRequest.asset as LoadingCatalog;
            if (catalog == null) throw new InvalidOperationException("Не найден каталог ресурсов загрузки.");
            content = catalog.Resolve(config.contentKey);
            string saveFile = Path.Combine(UnityEngine.Application.persistentDataPath, "Blinopek", "local-core-v1.json");
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(EditorValidationSavePath))
            { saveFile = EditorValidationSavePath; EditorValidationSavePath = null; }
#endif
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-blinopekSmoke") >= 0 && Array.IndexOf(args, "-blinopekSave") < 0)
                throw new InvalidOperationException("Smoke tests require an isolated -blinopekSave path.");
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-blinopekSave") saveFile = args[i + 1];
            loading.Report("Читаем сохранение", .3f);
            var store = new FileGameStore(saveFile);
            var read = store.PrefetchAsync();
            while (!read.IsCompleted) yield return null;
            read.GetAwaiter().GetResult();
            // JSON/Unity APIs and all model publication stay on the Unity thread.
            var controller = new GameController(store, new LocalDevelopmentGateway(), content.balance, true);
            foreach (float progress in controller.RestoreSteps())
            { loading.Report("Восстанавливаем смену", .32f + .18f * progress); yield return null; }
            if (SceneManager.GetActiveScene().name != config.scene)
            {
                if (!UnityEngine.Application.CanStreamedLevelBeLoaded(config.scene)) throw new InvalidOperationException("Сцена отсутствует в сборке: " + config.scene);
                var scene = SceneManager.LoadSceneAsync(config.scene);
                while (!scene.isDone) { loading.Report("Открываем кафе", .5f + .12f * Mathf.Clamp01(scene.progress / .9f)); yield return null; }
            }
            view = new GameObject("CafeView").AddComponent<GameView>();
            view.SetLoading(true);
            var initialize = view.InitializeAsync(controller, content, (stage, progress) => loading.Report(stage, .62f + .36f * progress));
            try { while (initialize.MoveNext()) yield return initialize.Current; }
            finally { (initialize as IDisposable)?.Dispose(); }
            view.LoadingTransition = BeginTransition;
            loading.Report("Завершаем загрузку", 1); yield return null;
        }
        private void BeginTransition(Action action, string stage)
        {
            if (busy || view == null) return;
            busy = true; view.SetLoading(true); loading.Show(stage);
            loading.SetMinimumVisibleTime(config.minimumVisibleSeconds);
            StartCoroutine(Guard(Transition(action, stage), () => BeginTransition(action, stage), true));
        }
        private IEnumerator Transition(Action action, string stage)
        {
            yield return null; yield return null;
            loading.Report(stage, .25f); action();
            yield return null;
            loading.Report("Освобождаем временные ресурсы", .75f);
            if (config.unloadUnusedAssets) yield return Resources.UnloadUnusedAssets();
            loading.Report("Готово", 1); yield return null;
        }
        private IEnumerator ReturnFromFailure()
        {
            busy = true;
            try { yield return loading.WaitMinimumVisibleTime(config?.minimumVisibleSeconds ?? 1f); }
            finally { busy = false; }
            if (view != null) view.SetLoading(false);
            loading.Hide();
        }
        private IEnumerator Guard(IEnumerator work, Action retry, bool canReturn = false)
        {
            Exception failure = null;
            try
            {
                while (true)
                {
                    bool next;
                    try { next = work.MoveNext(); }
                    catch (Exception ex) { failure = ex; break; }
                    if (!next) break;
                    yield return work.Current;
                }
                if (failure == null)
                {
                    var minimum = loading.Complete(config?.minimumVisibleSeconds ?? 1f);
                    while (minimum.MoveNext()) yield return minimum.Current;
                }
            }
            finally { (work as IDisposable)?.Dispose(); busy = false; }
            if (failure != null)
            {
                loading.Fail(failure, retry, canReturn ? (Action)(() => { if (!busy) StartCoroutine(ReturnFromFailure()); }) : null);
                yield break;
            }
            if (view != null) view.SetLoading(false);
            loading.Hide();
#if UNITY_EDITOR || DEVELOPMENT_BUILD || BLINOPEK_QA
            if (!canReturn && view != null && Array.IndexOf(Environment.GetCommandLineArgs(), "-blinopekSmoke") >= 0)
                view.gameObject.AddComponent<CoreSmokeDriver>().View = view;
#endif
        }
    }
}
