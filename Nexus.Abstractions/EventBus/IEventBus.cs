namespace Nexus.Abstractions.EventBus;

public interface IEventBus
{
    Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default);
    IDisposable Subscribe<T>(Func<T, Task> handler);
}
