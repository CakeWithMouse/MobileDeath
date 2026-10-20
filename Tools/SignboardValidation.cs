using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blinopek.Application;
using Blinopek.Domain;
using Blinopek.Infrastructure;
using Blinopek.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Run from an Editor folder in an isolated Unity project. Never opens the player's save.
public static class SignboardValidation
{
    private static readonly List<string> checks = new List<string>();
    private static GameSystem view;
    private static Camera camera;
    private static string output;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks.Add("PASS " + message); }
    private static GameObject Find(string name) => view.GetComponentsInChildren<Transform>(true).Single(t => t.name == name).gameObject;
    private static void Click(string name)
    {
        var button = Find(name).GetComponent<Button>(); Check(button.IsInteractable() && button.gameObject.activeInHierarchy, name + " available");
        ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        view.Refresh(); Canvas.ForceUpdateCanvases();
    }
    private static void Render(string name, string hit = null, int width = 1600, int height = 900)
    {
        view.Refresh(); var canvas = view.GetComponentInChildren<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        canvas.GetComponent<CanvasScaler>().enabled = false; canvas.scaleFactor = 1;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        var root = Find("SafeArea").transform; root.localPosition = Vector3.zero; root.localScale = Vector3.one * Mathf.Min(width/1600f,height/900f);
        Canvas.ForceUpdateCanvases(); camera.Render();
        if (hit != null) {
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(camera, Find(hit).transform.position) };
            var hits = new List<RaycastResult>(); canvas.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Check(hits.Count > 0 && hits[0].gameObject == Find(hit), hit + " receives the top pointer hit");
        }
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(width,height,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
        File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
    }
    public static void Run()
    {
        output = Path.GetFullPath("../../output/signboard-validation"); Directory.CreateDirectory(output);
        try {
            var art = AssetDatabase.LoadAssetAtPath<GameContent>("Assets/Blinopek/Content/Gameplay.asset");
            Check(art.signboardFont != null && art.signboardFont.name == "YesevaOne-Regular", "Yeseva One is linked");
            Check(art.signboardShader != null && !ShaderUtil.ShaderHasError(art.signboardShader), "Paint shader compiles");
            foreach (char c in "БлинопёкЁжик") Check(art.signboardFont.HasCharacter(c), "Font contains " + c);
            string save = Path.Combine(output,"isolated-"+Guid.NewGuid().ToString("N")+".json");
            var game = new GameController(new FileGameStore(save),new LocalDevelopmentGateway(),art.balance);
            view = new GameObject("Signboard validation").AddComponent<GameSystem>(); view.Initialize(game,art);
            camera = new GameObject("Screenshot camera").AddComponent<Camera>(); camera.orthographic = true;
            camera.transform.position = new Vector3(0,0,-10); camera.clearFlags = CameraClearFlags.SolidColor;
            Render("01-main-menu","CafeName"); Click("CafeName");
            var input = Find("CafeNameInput").GetComponent<InputField>(); input.text = "Мёд и масло";
            Render("02-rename-dialog","SaveCafeName"); Click("SaveCafeName");
            Check(game.CafeName == "Мёд и масло" && game.Session == null && game.Paused,"Rename saves without starting the day");
            Render("03-renamed-menu","CafeName");
            UnityEngine.Object.DestroyImmediate(view.gameObject);
            game = new GameController(new FileGameStore(save),new LocalDevelopmentGateway(),art.balance);
            Check(game.CafeName == "Мёд и масло","Player name survives actual disk reload");
            view = new GameObject("Reloaded signboard").AddComponent<GameSystem>(); view.Initialize(game,art);
            game.RenameCafe("Блинная у тёплого окна",out _); Render("04-long-name","CafeName");
            game.RenameCafe("Блинопёк",out _); Render("05-mobile", "CafeName", 960,540);
            Check(!ShaderUtil.ShaderHasError(art.signboardShader),"Paint shader renders without errors");
            File.WriteAllLines(Path.Combine(output,"PASSED.txt"),checks);
            Debug.Log("SIGNBOARD_VALIDATION_PASSED " + checks.Count); EditorApplication.Exit(0);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output,"FAILED.txt"),ex.ToString()); Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
