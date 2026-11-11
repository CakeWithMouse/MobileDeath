using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blinopek.Domain;
namespace Blinopek.Application
{
    public class GameApiException : Exception
    {
        public readonly long Status;
        public readonly string Code;
        public GameApiException(long status,string code,string message):base(message){Status=status;Code=code;}
    }
    [Serializable] public sealed class WireProfile
    {
        public int day, scrapers, ownedIngredients, helpersOwned, helpersEnabled, ratingTotal, ratingCount;
        public string coins, honey;
        public bool patienceUpgrade, darkTablesUpgrade, tutorialComplete, demonstrationSeen;
        public Profile ToDomain() => new Profile {day=day,coins=int.Parse(coins),honey=int.Parse(honey),scrapers=scrapers,ownedIngredients=ownedIngredients,helpersOwned=helpersOwned,helpersEnabled=helpersEnabled,ratingTotal=ratingTotal,ratingCount=ratingCount,patienceUpgrade=patienceUpgrade,darkTablesUpgrade=darkTablesUpgrade,tutorialComplete=tutorialComplete,demonstrationSeen=demonstrationSeen};
    }
    [Serializable] public sealed class PlayerReply { public int revision; public string cafeName, activeDayId; public WireProfile profile; }
    [Serializable] public sealed class DayReply { public string id,state,errorCode; public int revision,seed,generation,ackSeq,batchCount; public BalanceRules rules; public WireProfile snapshot; public PlayerReply receipt; }
    [Serializable] public sealed class CommandReply { public int firstSeq,ackSeq; public List<GameCommand> commands; }
    [Serializable] public sealed class NetworkOperation
    {
        public string operationId, itemId, action, name;
        public int expectedRevision,quantity=1,ingredient,generation=1,finalSeq;
        public bool enabled;
    }
    [Serializable] public sealed class BatchRequest { public int generation=1,firstSeq; public List<GameCommand> commands; }
    [Serializable] public sealed class BatchReply { public int ackSeq,batchNo; }
    public interface IGameApi
    {
        Task<PlayerReply> Player();
        Task<DayReply> Start(NetworkOperation operation);
        Task<DayReply> Day(string id);
        Task<CommandReply> Commands(string id,int after);
        Task<BatchReply> Batch(string id,int batch,BatchRequest request);
        Task<DayReply> Complete(string id,NetworkOperation operation);
        Task<PlayerReply> Cancel(string id,NetworkOperation operation);
        Task<PlayerReply> Purchase(NetworkOperation operation);
        Task<PlayerReply> Rename(NetworkOperation operation);
        Task<PlayerReply> Action(NetworkOperation operation);
    }
}
