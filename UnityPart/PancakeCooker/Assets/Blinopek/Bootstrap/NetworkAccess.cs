using System;
using Blinopek.Application;
using Blinopek.Infrastructure;
using UnityEngine;
namespace Blinopek.Bootstrap
{
    public sealed class NetworkAccess : MonoBehaviour
    {
        public HttpGameApi Api {get;private set;}
        public string OwnerId {get;private set;}
        public bool Ready {get;private set;}
        public string ServerKey {get;private set;}
        private GameController controller;
        private string login="",password="",email="",message="";
        private bool busy,loginVisible=true,confirmCancel;
        private float nextSync;
        public void Initialize(string url)
        {
            Api=new HttpGameApi(url);
            using(var sha=System.Security.Cryptography.SHA256.Create())ServerKey=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Api.BaseUrl))).Replace("-","").Substring(0,24);
            OwnerId=PlayerPrefs.GetString("Blinopek.Owner."+ServerKey,"");
            Api.Unauthorized+=()=>{loginVisible=true;controller?.SetPause(PauseReason.Menu,true);};
        }
        public void Attach(GameController game){controller=game;}
        private async void Login(bool register)
        {
            if(busy)return;busy=true;
            try
            {
                var credentials=new AuthRequest{login=login,password=password,email=email};
                if(register)await Api.Send<AuthReply>("POST","/auth/register",credentials);
                var response=await Api.Send<AuthReply>("POST","/auth/login",credentials);
                if(controller!=null&&OwnerId!=response.user.id.ToString())throw new InvalidOperationException("Для этой смены войдите в прежний аккаунт. Для смены аккаунта перезапустите игру.");
                OwnerId=response.user.id.ToString();Api.Token=response.token;password="";
                PlayerPrefs.SetString("Blinopek.Owner."+ServerKey,OwnerId);PlayerPrefs.Save();Ready=true;loginVisible=false;message="";
                if(controller!=null)await controller.RefreshOnline();
            }
            catch(Exception ex){message=ex.Message;}
            finally{busy=false;}
        }
        private void Update(){if(controller!=null&&!controller.NetworkBusy&&!string.IsNullOrEmpty(Api.Token)&&Time.unscaledTime>=nextSync){nextSync=Time.unscaledTime+15;_ = controller.SyncOnline();}}
        private async void Logout(){try{await Api.Send<AuthReply>("POST","/auth/logout");}catch(Exception ex){message=ex.Message;}finally{Api.Token=null;loginVisible=true;controller?.SetPause(PauseReason.Menu,true);}}
        private void OnGUI()
        {
            var scale=Mathf.Max(.6f,Mathf.Min(Screen.width/1000f,Screen.height/620f));var previous=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            if(loginVisible)
            {
                GUILayout.BeginArea(new Rect(20,20,510,550),GUI.skin.box);
                GUILayout.Label("Блинопёк — вход на сервер");GUILayout.Label(Api?.BaseUrl??"");
                GUILayout.Label("Логин");login=GUILayout.TextField(login,32);GUILayout.Label("Пароль");password=GUILayout.PasswordField(password,'*',128);
                GUILayout.Label("Email для регистрации");email=GUILayout.TextField(email,254);
                GUI.enabled=!busy;if(GUILayout.Button("Войти",GUILayout.Height(42)))Login(false);if(GUILayout.Button("Создать аккаунт",GUILayout.Height(42)))Login(true);
                if(!string.IsNullOrEmpty(OwnerId)&&GUILayout.Button("Продолжить сохранённую смену без сети",GUILayout.Height(42))){Ready=true;loginVisible=false;}
                GUI.enabled=true;GUILayout.Label(message);GUILayout.Label("Пароль и токен не сохраняются на диск.");GUILayout.EndArea();
            }
            else if(controller!=null)
            {
                GUILayout.BeginArea(new Rect(8,8,540,confirmCancel?160:92),GUI.skin.box);
                GUILayout.Label(controller.NetworkBusy?"Обмен с сервером…":controller.AwaitingSync?"Результат предварительный. Ожидает подтверждения.":"Серверный профиль · аккаунт "+OwnerId);
                GUILayout.BeginHorizontal();GUI.enabled=!controller.NetworkBusy;
                if(GUILayout.Button("Синхронизация"))_ = controller.RefreshOnline();
                if(GUILayout.Button("Отменить смену"))confirmCancel=!confirmCancel;
                if(GUILayout.Button(string.IsNullOrEmpty(Api.Token)?"Вход":"Выход")){if(string.IsNullOrEmpty(Api.Token))loginVisible=true;else Logout();}
                GUI.enabled=true;GUILayout.EndHorizontal();
                if(confirmCancel){GUILayout.Label("Доход этой попытки не будет начислен.");if(GUILayout.Button("Подтвердить отмену")){confirmCancel=false;_ = controller.CancelOnline();}}
                GUILayout.EndArea();
            }
            GUI.matrix=previous;
        }
    }
    [Serializable] public sealed class NetworkSettings {public bool enabled=true;public string baseUrl="http://127.0.0.1:3005";}
}
