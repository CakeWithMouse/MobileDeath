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
    public sealed class GameStartup : MonoBehaviour
    {
        // 1) Владеет объектом.
        // Постоянных дочерних сервисов не хранит: создаёт GameController и передаёт его GameSystem.

        // 2) Общается через API.
        // Готовый ресурс из Unity; GameStartup не создаёт и не уничтожает его.
        public GameContent content;

        // 3) Внутренние поля и объекты класса.
        private string error;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || BLINOPEK_QA
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureSmokeRun()
        {
            // Hidden QA players must reach Start and the asynchronous Cafe load without acquiring focus.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-blinopekSmoke") >= 0)
                UnityEngine.Application.runInBackground = true;
        }
#endif
        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            UnityEngine.Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            GameController controller = null;
            try
            {
                if (content == null) throw new InvalidOperationException("Gameplay content is missing. Run Blinopek > Prepare gameplay scenes.");
                string saveFile = Path.Combine(UnityEngine.Application.persistentDataPath, "Blinopek", "local-core-v1.json");
                // QA can use an isolated save without touching the player's test progress.
                string[] args = Environment.GetCommandLineArgs();
                if (Array.IndexOf(args, "-blinopekSmoke") >= 0 && Array.IndexOf(args, "-blinopekSave") < 0)
                    throw new InvalidOperationException("Smoke tests require an isolated -blinopekSave path.");
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-blinopekSave") saveFile = args[i + 1];
                controller = new GameController(new FileGameStore(saveFile), new LocalDevelopmentGateway(), content.balance);
            }
            catch (Exception ex) { error = ex.ToString(); Debug.LogException(ex); }
            if (controller == null) yield break;
            yield return SceneManager.LoadSceneAsync("Cafe");
            var system = new GameObject("GameSystem").AddComponent<GameSystem>(); system.Initialize(controller, content);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || BLINOPEK_QA
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-blinopekSmoke") >= 0)
                system.gameObject.AddComponent<CoreSmokeDriver>().System = system;
#endif
        }
        private void OnGUI()
        {
            if (error == null) return;
            GUI.Box(new Rect(25, 25, Screen.width - 50, Screen.height - 50), "Не удалось открыть блинную\n\n" + error);
        }
    }
}
