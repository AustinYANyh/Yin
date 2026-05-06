using System.Collections.Concurrent;

namespace Yin.Web.Services;

public sealed class StaRenderDispatcher : IDisposable
{
    private readonly BlockingCollection<RenderWorkItem> _queue = new();
    private readonly Thread _thread;

    public StaRenderDispatcher()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Yin WPF Render STA"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> action)
    {
        var item = new RenderWorkItem(() => action()!, TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(item);
        return item.Task.ContinueWith(t => (T)t.GetAwaiter().GetResult(), TaskScheduler.Default);
    }

    private void Run()
    {
        foreach (RenderWorkItem item in _queue.GetConsumingEnumerable())
        {
            try
            {
                item.SetResult(item.Action());
            }
            catch (Exception ex)
            {
                item.SetException(ex);
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _queue.Dispose();
    }

    private sealed class RenderWorkItem : TaskCompletionSource<object>
    {
        public RenderWorkItem(Func<object> action, TaskCreationOptions options) : base(options)
        {
            Action = action;
        }

        public Func<object> Action { get; }
    }
}
