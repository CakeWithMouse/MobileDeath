using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blinopek.Domain;
namespace Blinopek.Application
{
    public sealed partial class GameController
    {
        private IGameApi api;
        public bool IsOnline => gateway.Environment == "online";
        public bool NetworkBusy {get;private set;}
        public bool AwaitingSync => IsOnline && Session != null && Session.Phase == DayPhase.Result && !saved.settled;
        public event Action OnlineDayReady;
        public void Connect(IGameApi value) {api=value;}
        public void SetNetworkError(string message) {StorageError=message;Changed?.Invoke();}
        private async Task NetworkWork(Func<Task> work)
        {
            if(NetworkBusy||StorageBlocked||api==null)return;
            NetworkBusy=true;Changed?.Invoke();
            try{await work();StorageError=null;}
            catch(Exception ex){
                if(ex is System.IO.IOException || ex is UnauthorizedAccessException)StorageBlocked=true;
                if(ex is GameApiException fault && (fault.Status==400||fault.Status==404||fault.Status==409||fault.Status==422))
                {ClearPending();try{PersistRequired();}catch{StorageBlocked=true;}}
                StorageError="Сеть: "+ex.Message;
            }
            finally{NetworkBusy=false;Changed?.Invoke();}
        }
        private void PersistRequired() {store.Save(saved);StorageError=null;}
        private NetworkOperation Pending(string kind)
        {
            if(saved.pendingOperation!=null && !string.IsNullOrEmpty(saved.pendingOperation.operationId))
            {if(saved.pendingKind!=kind)throw new InvalidOperationException("Сначала повторите незавершённую операцию.");return saved.pendingOperation;}
            saved.pendingKind=kind;saved.pendingOperation=new NetworkOperation{operationId=Guid.NewGuid().ToString(),expectedRevision=saved.revision};return saved.pendingOperation;
        }
        private void ApplyPlayer(PlayerReply p)
        {
            if(p.revision < saved.revision)return;
            saved.profile=p.profile.ToDomain();saved.revision=p.revision;saved.cafeName=p.cafeName;
        }
        private void ClearPending(){saved.pendingOperation=null;saved.pendingKind=null;}
        public Task RefreshOnline() => NetworkWork(async()=>{
            if(saved.pendingKind=="start")await StartOnlineCore();
            else if(saved.pendingKind=="purchase"||saved.pendingKind=="action"||saved.pendingKind=="rename")await RetryProfileOperation();
            else if(saved.pendingKind=="cancel")await CancelOnlineCore();
            var player=await api.Player();
            if(!string.IsNullOrEmpty(saved.attemptId)&&!saved.settled)
            {
                var day=await api.Day(saved.attemptId);
                if(day.state=="settled"){ApplyPlayer(day.receipt);saved.settled=true;ClearPending();PersistRequired();}
                else if(day.state=="cancelled"){saved.attemptId=null;saved.sessionStart=null;saved.commands.Clear();saved.awaitingDay=true;Session=null;ClearPending();ApplyPlayer(player);PersistRequired();}
                else if(day.state=="rejected")throw new InvalidOperationException("Сервер отклонил журнал. Можно отменить смену.");
            }
            else if(!string.IsNullOrEmpty(player.activeDayId))await RestoreOnlineDay(await api.Day(player.activeDayId));
            else{ApplyPlayer(player);PersistRequired();}
            if(AwaitingSync)await SyncOnlineCore();
        });
        private async Task RestoreOnlineDay(DayReply d)
        {
            saved.attemptId=d.id;saved.seed=d.seed;saved.rules=d.rules;saved.sessionStart=d.snapshot.ToDomain();saved.commands=new List<GameCommand>();saved.settled=false;saved.awaitingDay=false;saved.ackSeq=0;saved.batchCount=d.batchCount;
            while(saved.commands.Count<d.ackSeq){var part=await api.Commands(d.id,saved.commands.Count);if(part.commands==null||part.commands.Count==0)throw new InvalidOperationException("Неполный журнал сервера.");saved.commands.AddRange(part.commands);}
            saved.ackSeq=d.ackSeq;saved.pendingBatchSize=0;Session=SessionJournal.Replay(saved);PersistRequired();
        }
        public Task StartOnline() => NetworkWork(StartOnlineCore);
        private async Task StartOnlineCore()
        {
            if(AwaitingSync){await SyncOnlineCore();if(AwaitingSync)return;}
            if(Session!=null&&Session.Phase!=DayPhase.Result)return;
            var operation=Pending("start");PersistRequired();var day=await api.Start(operation);
            saved.attemptId=day.id;saved.seed=day.seed;saved.rules=day.rules;saved.profile=day.snapshot.ToDomain();saved.sessionStart=saved.profile.Copy();saved.revision=day.revision;saved.commands=new List<GameCommand>();saved.ackSeq=0;saved.batchCount=0;saved.settled=false;saved.awaitingDay=false;
            saved.pendingBatchSize=0;
            Session=new DaySession(saved.sessionStart,saved.rules,saved.seed,false,false);ClearPending();PersistRequired();
            Pauses &= ~PauseReason.Menu;
            Dispatch(new GameCommand(ActionKind.EnableFreeCooking));Dispatch(new GameCommand(ActionKind.EnableManualServing));PersistRequired();
            DayStarted?.Invoke();OnlineDayReady?.Invoke();
        }
        public Task SyncOnline() => NetworkWork(SyncOnlineCore);
        private async Task SyncOnlineCore()
        {
            if(string.IsNullOrEmpty(saved.attemptId)||saved.settled)return;
            PersistRequired();
            while(saved.ackSeq<saved.commands.Count)
            {
                if(saved.pendingBatchSize==0){saved.pendingBatchSize=Math.Min(512,saved.commands.Count-saved.ackSeq);PersistRequired();}
                var batch=new BatchRequest{firstSeq=saved.ackSeq+1,commands=saved.commands.GetRange(saved.ackSeq,saved.pendingBatchSize)};
                var ack=await api.Batch(saved.attemptId,saved.batchCount,batch);
                if(ack.ackSeq!=saved.ackSeq+saved.pendingBatchSize||ack.batchNo!=saved.batchCount)throw new InvalidOperationException("Некорректное подтверждение пакета.");
                saved.ackSeq=ack.ackSeq;saved.batchCount=ack.batchNo+1;saved.pendingBatchSize=0;PersistRequired();
            }
            if(Session==null||Session.Phase!=DayPhase.Result)return;
            var operation=Pending("complete");operation.finalSeq=saved.commands.Count;PersistRequired();
            var result=await api.Complete(saved.attemptId,operation);
            if(result.state=="settled"){ApplyPlayer(result.receipt);saved.settled=true;ClearPending();PersistRequired();}
            else if(result.state=="rejected")throw new InvalidOperationException("Сервер отклонил журнал. Можно отменить смену.");
        }
        public Task BuyOnline(ShopItem item,int ingredient) => NetworkWork(async()=>{
            if(!string.IsNullOrEmpty(saved.attemptId)&&!saved.settled)throw new InvalidOperationException("Сначала завершите или отмените смену.");
            if(saved.pendingOperation!=null&&!string.IsNullOrEmpty(saved.pendingOperation.operationId)){await RetryProfileOperation();return;}
            var kind=item==ShopItem.ToggleHelper?"action":"purchase";var op=Pending(kind);
            op.itemId=item==ShopItem.Ingredient?"ingredient_"+ingredient:item==ShopItem.Helper?"helper_"+ingredient:item==ShopItem.Scraper?"scraper":item==ShopItem.Patience?"patience":"dark_tables";
            op.action="set_helper_enabled";op.ingredient=ingredient;op.enabled=(Profile.helpersEnabled&(1<<ingredient))==0;PersistRequired();
            await RetryProfileOperation();PurchaseApplied?.Invoke(item);
        });
        private async Task RetryProfileOperation()
        {
            var op=saved.pendingOperation;PlayerReply response;
            if(saved.pendingKind=="purchase")response=await api.Purchase(op);
            else if(saved.pendingKind=="action")response=await api.Action(op);
            else if(saved.pendingKind=="rename")response=await api.Rename(op);
            else throw new InvalidOperationException("Незавершённая операция другого типа.");
            ApplyPlayer(response);ClearPending();PersistRequired();
        }
        public Task RenameOnline(string name) => NetworkWork(async()=>{if(!ValidateCafeName(name,out var normalized,out var error))throw new InvalidOperationException(error);var op=Pending("rename");op.name=normalized;PersistRequired();await RetryProfileOperation();});
        public Task CancelOnline() => NetworkWork(CancelOnlineCore);
        private async Task CancelOnlineCore()
        {
            if(string.IsNullOrEmpty(saved.attemptId))return;
            if(saved.pendingKind=="complete")ClearPending();var op=Pending("cancel");PersistRequired();var response=await api.Cancel(saved.attemptId,op);
            ApplyPlayer(response);saved.attemptId=null;saved.sessionStart=null;saved.commands.Clear();saved.awaitingDay=true;saved.settled=false;Session=null;ClearPending();PersistRequired();
        }
    }
}
