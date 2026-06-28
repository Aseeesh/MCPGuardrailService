namespace GuardrailApi.Pipeline.CircuitBreaker;

public class CircuitBreaker
{
    private enum State { Closed, Open, HalfOpen }

    private State _state = State.Closed;
    private int _failureCount;
    private readonly int _failureThreshold;
    private readonly TimeSpan _recoveryTime;
    private DateTime _lastFailure = DateTime.MinValue;
    private readonly object _lock = new();

    public CircuitBreaker(int failureThreshold = 5, int recoveryTimeSeconds = 30)
    {
        _failureThreshold = failureThreshold;
        _recoveryTime = TimeSpan.FromSeconds(recoveryTimeSeconds);
    }

    public bool AllowRequest()
    {
        lock (_lock)
        {
            return _state switch
            {
                State.Closed => true,
                State.Open when DateTime.UtcNow - _lastFailure >= _recoveryTime => TransitionTo(State.HalfOpen),
                State.Open => false,
                State.HalfOpen => true,
                _ => true,
            };
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failureCount = 0;
            _state = State.Closed;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailure = DateTime.UtcNow;
            if (_failureCount >= _failureThreshold || _state == State.HalfOpen)
                _state = State.Open;
        }
    }

    private bool TransitionTo(State state)
    {
        _state = state;
        return true;
    }

    public CircuitBreakerStatus GetStatus() => new(
        _state.ToString(),
        _failureCount,
        _failureThreshold,
        _lastFailure == DateTime.MinValue ? null : _lastFailure);
}

public record CircuitBreakerStatus(
    string State,
    int FailureCount,
    int Threshold,
    DateTime? LastFailure);
