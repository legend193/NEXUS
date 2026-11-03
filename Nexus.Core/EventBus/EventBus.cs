using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.EventBus;

namespace Nexus.Core.EventBus;

public class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _lock = new();
    private readonly ILogger<EventBus>? _logger;

    public EventBus(ILogger<EventBus>? logger = null)
    {
        _logger = logger;
    }

    public async Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
    {
        if (@event == null) return;
        var eventType = typeof(T);

        List<Delegate>? handlersCopy = null;
        lock (_lock)
        {
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                handlersCopy = new List<Delegate>(handlers);
            }
        }

        if (handlersCopy != null && handlersCopy.Count > 0)
        {
            foreach (var handler in handlersCopy)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    if (handler is Func<T, Task> asyncFunc)
                    {
                        await asyncFunc(@event);
                    }
                    else if (handler is Action<T> action)
                    {
                        action(@event);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error handling event {EventType}", eventType.Name);
                }
            }
        }
    }

    public IDisposable Subscribe<T>(Func<T, Task> handler)
    {
        var eventType = typeof(T);
        lock (_lock)
        {
            var list = _handlers.GetOrAdd(eventType, _ => new List<Delegate>());
            list.Add(handler);
        }

        return new Unsubscriber(() =>
        {
            lock (_lock)
            {
                if (_handlers.TryGetValue(eventType, out var list))
                {
                    list.Remove(handler);
                }
            }
        });
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public Unsubscriber(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _unsubscribe();
            }
        }
    }
}
