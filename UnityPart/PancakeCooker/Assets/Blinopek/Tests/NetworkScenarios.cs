using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blinopek.Application;
using Blinopek.Domain;
namespace Blinopek.Tests
{
    public static partial class CoreScenarios
    {
        private sealed class ServerOnlyGateway : IDayGateway {public string Environment=>"online";public Profile Settle(DaySession s,Profile p)=>throw new Exception("Local settlement called");}
        private sealed class FakeGameApi : IGameApi
        {
            public bool FailBatch, RejectPurchase, LoseBatchReply;
            private int lostBatch=-1,lostSize;
            public int Completions;
            public readonly List<GameCommand> Received=new List<GameCommand>();
            public WireProfile Profile=new WireProfile{day=1,coins="3000",honey="0",ownedIngredients=1,tutorialComplete=true};
            public Task<PlayerReply> Player()=>Task.FromResult(new PlayerReply{profile=Profile,revision=1,cafeName="Test"});
            public Task<DayReply> Start(NetworkOperation op)=>Task.FromResult(new DayReply{id="attempt",state="active",snapshot=Profile,seed=4,rules=new BalanceRules{dayTicks=20},revision=2});
            public Task<DayReply> Day(string id)=>Task.FromResult(new DayReply{id=id,state="active"});
            public Task<CommandReply> Commands(string id,int after)=>Task.FromResult(new CommandReply{commands=new List<GameCommand>()});
            public Task<BatchReply> Batch(string id,int n,BatchRequest b){if(FailBatch)throw new GameApiException(503,"DOWN","down");if(n==lostBatch){Check(b.commands.Count==lostSize,"Retry changed an accepted batch boundary");lostBatch=-1;return Task.FromResult(new BatchReply{ackSeq=b.firstSeq+b.commands.Count-1,batchNo=n});}foreach(var c in b.commands)Received.Add(c.Copy());if(LoseBatchReply){LoseBatchReply=false;lostBatch=n;lostSize=b.commands.Count;throw new GameApiException(503,"LOST_REPLY","reply lost");}return Task.FromResult(new BatchReply{ackSeq=b.firstSeq+b.commands.Count-1,batchNo=n});}
            public Task<DayReply> Complete(string id,NetworkOperation op){Completions++;return Task.FromResult(new DayReply{state="settled",receipt=new PlayerReply{revision=3,profile=new WireProfile{day=2,coins="2360",honey="0",ownedIngredients=1,tutorialComplete=true}}});}
            public Task<PlayerReply> Cancel(string id,NetworkOperation op)=>Player();
            public Task<PlayerReply> Purchase(NetworkOperation op){if(RejectPurchase)throw new GameApiException(409,"INSUFFICIENT_FUNDS","funds");return Player();}
            public Task<PlayerReply> Rename(NetworkOperation op)=>Player();
            public Task<PlayerReply> Action(NetworkOperation op)=>Player();
        }
        private static void OnlineJournalAndSettlement()
        {
            var store=new MemoryStore{Data=new SavedGame{schema=2,environment="online",ownerId="1",profile=new Profile{tutorialComplete=true},rules=new BalanceRules(),awaitingDay=true,revision=1}};
            var game=new GameController(store,new ServerOnlyGateway(),store.Data.rules);var api=new FakeGameApi();game.Connect(api);game.StartOnline().GetAwaiter().GetResult();
            Check(game.Session!=null,"Online start missing");game.Dispatch(new GameCommand(ActionKind.Tick,1));game.SyncOnline().GetAwaiter().GetResult();int count=api.Received.Count;game.Dispatch(new GameCommand(ActionKind.Tick,1));game.SyncOnline().GetAwaiter().GetResult();Check(api.Received.Count==count+1&&api.Received[count-1].a==1,"Acknowledged tick changed");
            api.LoseBatchReply=true;game.Dispatch(new GameCommand(ActionKind.Tick,1));game.SyncOnline().GetAwaiter().GetResult();Check(store.Data.pendingBatchSize==1,"Lost reply did not preserve batch");
            game=new GameController(store,new ServerOnlyGateway(),store.Data.rules);game.Connect(api);game.Dispatch(new GameCommand(ActionKind.Tick,1));game.SyncOnline().GetAwaiter().GetResult();Check(game.StorageError==null&&store.Data.pendingBatchSize==0,"Batch retry failed after restart and new input");
            game.Dispatch(new GameCommand(ActionKind.Tick,100));Check(game.AwaitingSync&&game.Profile.coins==3000,"Client awarded unconfirmed coins");api.FailBatch=true;game.SyncOnline().GetAwaiter().GetResult();Check(game.AwaitingSync&&game.Profile.coins==3000,"Network failure lost pending result");api.FailBatch=false;game.SyncOnline().GetAwaiter().GetResult();Check(!game.AwaitingSync&&game.Profile.coins==2360&&game.Profile.day==2,"Server receipt not applied");game.SyncOnline().GetAwaiter().GetResult();Check(api.Completions==1,"Completion repeated after receipt");
        }
        private static void OnlineRejectedOperation()
        {
            var store=new MemoryStore{Data=new SavedGame{schema=2,environment="online",ownerId="1",profile=new Profile(),rules=new BalanceRules(),awaitingDay=true,revision=1}};var game=new GameController(store,new ServerOnlyGateway(),store.Data.rules);game.Connect(new FakeGameApi{RejectPurchase=true});game.BuyOnline(ShopItem.Scraper,0).GetAwaiter().GetResult();Check(store.Data.pendingOperation==null&&game.Profile.scrapers==0&&game.Profile.coins==3000,"Rejected purchase stuck or changed profile");
        }
    }
}
