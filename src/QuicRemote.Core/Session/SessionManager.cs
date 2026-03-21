namespace QuicRemote.Core.Session;

public enum SessionState
{
    Idle,
    Connecting,
    Connected,
    Authenticating,
    Active,
    Disconnecting,
    Disconnected,
    Failed
}

public class SessionInfo
{
    public Guid SessionId { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public SessionState State { get; set; }
}

public class SessionManager : IAsyncDisposable
{
    private readonly Dictionary<Guid, SessionInfo> _sessions = new();
    private readonly object _lock = new();
    private bool _disposed;

    public event EventHandler<SessionInfo>? SessionStateChanged;

    public SessionInfo CreateSession(string deviceId)
    {
        var session = new SessionInfo
        {
            SessionId = Guid.NewGuid(),
            DeviceId = deviceId,
            CreatedAt = DateTime.UtcNow,
            State = SessionState.Idle
        };

        lock (_lock)
        {
            _sessions[session.SessionId] = session;
        }

        return session;
    }

    public void UpdateState(Guid sessionId, SessionState newState)
    {
        SessionInfo? session;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
                return;
            session.State = newState;
        }

        SessionStateChanged?.Invoke(this, session);
    }

    public SessionInfo? GetSession(Guid sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }
    }

    public void RemoveSession(Guid sessionId)
    {
        lock (_lock)
        {
            _sessions.Remove(sessionId);
        }
    }

    public IReadOnlyList<SessionInfo> GetActiveSessions()
    {
        lock (_lock)
        {
            return _sessions.Values
                .Where(s => s.State is SessionState.Active or SessionState.Connected or SessionState.Authenticating)
                .ToList();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        lock (_lock)
        {
            _sessions.Clear();
        }

        return ValueTask.CompletedTask;
    }
}
