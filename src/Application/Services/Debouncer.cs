namespace Application.Services;

public class Debouncer
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource _cts;

    public Debouncer(TimeSpan delay)
    {
        _delay = delay;
    }

    public void Debounce(Func<Task> action)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Delay(_delay, token).ContinueWith(async _ =>
        {
            if (!token.IsCancellationRequested)
                await action();
        }, token);
    }
    
    public void Cancel() => _cts.Cancel();
}