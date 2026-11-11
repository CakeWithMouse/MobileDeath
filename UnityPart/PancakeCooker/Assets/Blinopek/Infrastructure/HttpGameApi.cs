using System;
using System.Text;
using System.Threading.Tasks;
using Blinopek.Application;
using Blinopek.Domain;
using UnityEngine;
using UnityEngine.Networking;
namespace Blinopek.Infrastructure
{
    public sealed class ApiException : GameApiException { public ApiException(long status,string code):base(status,code,status==401?"Войдите в тот же аккаунт для синхронизации.":code){} }
    [Serializable] public sealed class AuthRequest {public string login,password,email;}
    [Serializable] public sealed class AuthUser {public int id;public string login;}
    [Serializable] public sealed class AuthReply {public string token,expiresAt;public AuthUser user;}
    [Serializable] public sealed class ConfigReply {public BalanceRules rules;}
    [Serializable] sealed class ErrorReply {public ApiError error;}
    [Serializable] sealed class ApiError {public string code,message;}
    public sealed class HttpGameApi : IGameApi
    {
        public string Token {get;set;}
        public readonly string BaseUrl;
        public event Action Unauthorized;
        public HttpGameApi(string url){var uri=new Uri(url);if(uri.Scheme!="http"&&uri.Scheme!="https")throw new ArgumentException("HTTP(S) address required");BaseUrl=url.TrimEnd('/');}
        public async Task<T> Send<T>(string method,string route,object body=null)
        {
            using(var request=new UnityWebRequest(BaseUrl+route,method))
            {
                request.downloadHandler=new DownloadHandlerBuffer();request.timeout=20;
                if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));request.SetRequestHeader("Content-Type","application/json");}
                if(!string.IsNullOrEmpty(Token))request.SetRequestHeader("Authorization","Bearer "+Token);
                var completion=new TaskCompletionSource<bool>();request.SendWebRequest().completed+=_=>completion.TrySetResult(true);await completion.Task;
                if(request.result!=UnityWebRequest.Result.Success){var code="Сервер недоступен. Повторите синхронизацию.";try{code=JsonUtility.FromJson<ErrorReply>(request.downloadHandler.text)?.error?.code??code;}catch{}
                    if(request.responseCode==401){Token=null;Unauthorized?.Invoke();}throw new ApiException(request.responseCode,code);}
                return JsonUtility.FromJson<T>(request.downloadHandler.text);
            }
        }
        public Task<PlayerReply> Player()=>Send<PlayerReply>("GET","/v1/player");
        public Task<DayReply> Start(NetworkOperation o)=>Send<DayReply>("POST","/v1/day-sessions",o);
        public Task<DayReply> Day(string id)=>Send<DayReply>("GET","/v1/day-sessions/"+id);
        public Task<CommandReply> Commands(string id,int after)=>Send<CommandReply>("GET","/v1/day-sessions/"+id+"/commands?afterSeq="+after);
        public Task<BatchReply> Batch(string id,int n,BatchRequest b)=>Send<BatchReply>("PUT","/v1/day-sessions/"+id+"/batches/"+n,b);
        public Task<DayReply> Complete(string id,NetworkOperation o)=>Send<DayReply>("POST","/v1/day-sessions/"+id+"/complete",o);
        public Task<PlayerReply> Cancel(string id,NetworkOperation o)=>Send<PlayerReply>("POST","/v1/day-sessions/"+id+"/cancel",o);
        public Task<PlayerReply> Purchase(NetworkOperation o)=>Send<PlayerReply>("POST","/v1/shop/purchases",o);
        public Task<PlayerReply> Rename(NetworkOperation o)=>Send<PlayerReply>("PATCH","/v1/player/cafe",o);
        public Task<PlayerReply> Action(NetworkOperation o)=>Send<PlayerReply>("POST","/v1/player/actions",o);
    }
    public sealed class OnlineDayGateway : IDayGateway
    {
        public string Environment=>"online";
        public Profile Settle(DaySession session,Profile current)=>throw new InvalidOperationException("Only the server settles online days.");
    }
}
