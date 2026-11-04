using System;
using System.IO;
using System.Linq;
using Blinopek.Bootstrap;
using Blinopek.Application;
using Blinopek.Infrastructure;
using Blinopek.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Copy this and MenuNavigationValidation.cs into Editor in an isolated validation project.
public static class LoadingValidation
{
    public static void Run()
    {
        try
        {
            var json = Resources.Load<TextAsset>("Loading/loading");
            var config = JsonUtility.FromJson<LoadingConfig>(json.text); config.Validate();
            var catalog = Resources.Load<LoadingCatalog>("Loading/catalog");
            var content = catalog.Resolve(config.contentKey);
            bool rejected = false;
            try { catalog.Resolve("missing"); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Missing keys must fail.");
            var view = new GameObject("Async view validation").AddComponent<GameView>();
            try
            {
                var store = new FileGameStore(Path.Combine(Path.GetTempPath(), "blinopek-loading-validation-" + Guid.NewGuid().ToString("N") + ".json"));
                store.PrefetchAsync().GetAwaiter().GetResult();
                var controller = new GameController(store, new LocalDevelopmentGateway(), content.balance, true);
                foreach (var progress in controller.RestoreSteps()) { }
                int frames = 0; float previous = 0;
                var initialize = view.InitializeAsync(controller, content, (stage, progress) => {
                    if (progress < previous) throw new Exception("Progress moved backwards."); previous = progress;
                });
                while (initialize.MoveNext()) frames++;
                if (frames < 20 || view.Controller != controller || !controller.Paused) throw new Exception("Async initialization did not finish at paused menu.");
                Action pending = null;
                view.LoadingTransition = (action, stage) => pending = action;
                view.GetComponentsInChildren<Button>(true).Single(button => button.name == "ContinueShift").onClick.Invoke();
                if (pending == null || controller.Session != null) throw new Exception("Shift bypassed the loading coordinator.");
                view.SetLoading(true); pending(); view.SetLoading(false); view.Refresh();
                if (controller.Session == null || controller.Paused) throw new Exception("Shift failed to start through loading.");
                Debug.Log("LOADING_VALIDATION_PASSED yields=" + frames);
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
            MenuNavigationValidation.Run();
        }
        catch (Exception ex) { Debug.LogException(ex); if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(1); else throw; }
    }
}
