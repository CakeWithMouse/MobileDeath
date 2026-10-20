using Blinopek.Domain;

namespace Blinopek.Application
{
    // Контракт чтения для представления: не предоставляет команд, сохранения или доступа к gateway.
    // Session и Profile — живые модели контроллера, а не неизменяемые копии.
    // Представление только читает их; все изменения проходят через GameSystem -> GameController.
    public interface IGameState
    {
        DaySession Session { get; }
        Profile Profile { get; }
        PauseReason Pauses { get; }
        bool Paused { get; }
        bool StorageBlocked { get; }
        string StorageError { get; }
        bool CanResetProgress { get; }
        string CafeName { get; }
    }
}
