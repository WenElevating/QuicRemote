namespace QuicRemote.Core.Session;

/// <summary>
/// Session state enumeration
/// </summary>
public enum SessionState
{
    Idle,
    Connecting,
    Connected,
    Authenticating,
    Active,
    Paused,
    Disconnecting,
    Disconnected,
    Failed,
    Reconnecting
}

/// <summary>
/// Session role enumeration
/// </summary>
public enum SessionRole
{
    /// <summary>
    /// Host (controlled device)
    /// </summary>
    Host,

    /// <summary>
    /// Controller (has input control)
    /// </summary>
    Controller,

    /// <summary>
    /// Viewer (view only, no control)
    /// </summary>
    Viewer
}

/// <summary>
/// Control permission levels
/// </summary>
public enum ControlPermission
{
    /// <summary>
    /// No control, view only
    /// </summary>
    None,

    /// <summary>
    /// Can send input events
    /// </summary>
    Input,

    /// <summary>
    /// Full control including clipboard and files
    /// </summary>
    Full
}

/// <summary>
/// Represents a connected client in a session
/// </summary>
public class ClientInfo
{
    public string ClientId { get; init; } = string.Empty;
    public SessionRole Role { get; set; } = SessionRole.Viewer;
    public ControlPermission Permission { get; set; } = ControlPermission.None;
    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;
    public DateTime? LastActivity { get; set; }
}

/// <summary>
/// Session information with multi-client support
/// </summary>
public class SessionInfo
{
    public Guid SessionId { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public SessionState State { get; set; }

    /// <summary>
    /// Connected clients in this session
    /// </summary>
    public Dictionary<string, ClientInfo> Clients { get; } = new();

    /// <summary>
    /// Client that currently has control
    /// </summary>
    public string? ActiveControllerId { get; set; }

    /// <summary>
    /// Queue of clients waiting for control
    /// </summary>
    public Queue<string> ControlRequestQueue { get; } = new();
}

/// <summary>
/// Event arguments for session events
/// </summary>
public class SessionEventArgs : EventArgs
{
    public SessionInfo Session { get; init; } = null!;
    public string? ClientId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Event arguments for permission change events
/// </summary>
public class PermissionEventArgs : SessionEventArgs
{
    public ControlPermission OldPermission { get; init; }
    public ControlPermission NewPermission { get; init; }
}

/// <summary>
/// Event arguments for role change events
/// </summary>
public class RoleEventArgs : SessionEventArgs
{
    public SessionRole OldRole { get; init; }
    public SessionRole NewRole { get; init; }
}

/// <summary>
/// Manages remote desktop sessions with multi-client support
/// </summary>
public class SessionManager : IAsyncDisposable, IDisposable
{
    private readonly Dictionary<Guid, SessionInfo> _sessions = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Raised when a session is started
    /// </summary>
    public event EventHandler<SessionEventArgs>? SessionStarted;

    /// <summary>
    /// Raised when a session ends
    /// </summary>
    public event EventHandler<SessionEventArgs>? SessionEnded;

    /// <summary>
    /// Raised when a session state changes
    /// </summary>
    public event EventHandler<SessionEventArgs>? SessionStateChanged;

    /// <summary>
    /// Raised when a client's role changes
    /// </summary>
    public event EventHandler<RoleEventArgs>? RoleChanged;

    /// <summary>
    /// Raised when a client's permission changes
    /// </summary>
    public event EventHandler<PermissionEventArgs>? PermissionChanged;

    /// <summary>
    /// Raised when a client requests control
    /// </summary>
    public event EventHandler<SessionEventArgs>? ControlRequested;

    /// <summary>
    /// Creates a new session
    /// </summary>
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

    /// <summary>
    /// Updates the session state
    /// </summary>
    public void UpdateState(Guid sessionId, SessionState newState)
    {
        SessionInfo? session;
        SessionState oldState;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
                return;
            oldState = session.State;
            session.State = newState;
        }

        SessionStateChanged?.Invoke(this, new SessionEventArgs { Session = session });

        if (newState == SessionState.Active && oldState != SessionState.Active)
        {
            SessionStarted?.Invoke(this, new SessionEventArgs { Session = session });
        }
        else if (newState == SessionState.Disconnected || newState == SessionState.Failed)
        {
            SessionEnded?.Invoke(this, new SessionEventArgs { Session = session });
        }
    }

    /// <summary>
    /// Gets a session by ID
    /// </summary>
    public SessionInfo? GetSession(Guid sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }
    }

    /// <summary>
    /// Removes a session
    /// </summary>
    public void RemoveSession(Guid sessionId)
    {
        SessionInfo? session;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
                return;
            _sessions.Remove(sessionId);
        }

        SessionEnded?.Invoke(this, new SessionEventArgs { Session = session });
    }

    /// <summary>
    /// Gets all active sessions
    /// </summary>
    public IReadOnlyList<SessionInfo> GetActiveSessions()
    {
        lock (_lock)
        {
            return _sessions.Values
                .Where(s => s.State is SessionState.Active or SessionState.Connected or SessionState.Authenticating)
                .ToList();
        }
    }

    /// <summary>
    /// Adds a client to a session
    /// </summary>
    public bool AddClient(Guid sessionId, string clientId, SessionRole role = SessionRole.Viewer)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return false;

            if (session.Clients.ContainsKey(clientId))
                return false;

            var clientInfo = new ClientInfo
            {
                ClientId = clientId,
                Role = role,
                Permission = role == SessionRole.Controller ? ControlPermission.Input : ControlPermission.None,
                ConnectedAt = DateTime.UtcNow
            };

            session.Clients[clientId] = clientInfo;
            return true;
        }
    }

    /// <summary>
    /// Removes a client from a session
    /// </summary>
    public bool RemoveClient(Guid sessionId, string clientId)
    {
        SessionInfo? session;
        ClientInfo? client;
        ControlPermission oldPermission = ControlPermission.None;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
                return false;

            if (!session.Clients.TryGetValue(clientId, out client))
                return false;

            oldPermission = client.Permission;
            session.Clients.Remove(clientId);

            // If this client was the active controller, clear it
            if (session.ActiveControllerId == clientId)
            {
                session.ActiveControllerId = null;
            }

            // Remove from control queue if present
            var queueCopy = session.ControlRequestQueue.ToList();
            queueCopy.Remove(clientId);
            session.ControlRequestQueue.Clear();
            foreach (var id in queueCopy)
            {
                session.ControlRequestQueue.Enqueue(id);
            }
        }

        // Notify permission change if the client had permissions
        if (oldPermission != ControlPermission.None)
        {
            PermissionChanged?.Invoke(this, new PermissionEventArgs
            {
                Session = session,
                ClientId = clientId,
                OldPermission = oldPermission,
                NewPermission = ControlPermission.None
            });
        }

        return true;
    }

    /// <summary>
    /// Requests control permission for a client
    /// </summary>
    public bool RequestControl(Guid sessionId, string clientId, ControlPermission requestedPermission)
    {
        SessionInfo? session;
        ClientInfo? client;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
                return false;

            if (!session.Clients.TryGetValue(clientId, out client))
                return false;

            // If no one has control, grant immediately
            if (string.IsNullOrEmpty(session.ActiveControllerId))
            {
                var oldPermission = client.Permission;
                client.Permission = requestedPermission;
                session.ActiveControllerId = clientId;

                PermissionChanged?.Invoke(this, new PermissionEventArgs
                {
                    Session = session,
                    ClientId = clientId,
                    OldPermission = oldPermission,
                    NewPermission = requestedPermission
                });

                return true;
            }

            // If this client already has control, update permission
            if (session.ActiveControllerId == clientId)
            {
                var oldPermission = client.Permission;
                client.Permission = requestedPermission;

                PermissionChanged?.Invoke(this, new PermissionEventArgs
                {
                    Session = session,
                    ClientId = clientId,
                    OldPermission = oldPermission,
                    NewPermission = requestedPermission
                });

                return true;
            }

            // Add to queue
            if (!session.ControlRequestQueue.Contains(clientId))
            {
                session.ControlRequestQueue.Enqueue(clientId);
            }
        }

        ControlRequested?.Invoke(this, new SessionEventArgs
        {
            Session = session,
            ClientId = clientId
        });

        return false; // Queued, not granted immediately
    }

    /// <summary>
    /// Releases control for a client
    /// </summary>
    public bool ReleaseControl(Guid sessionId, string clientId)
    {
        SessionInfo? session;
        ClientInfo? client;
        ControlPermission oldPermission = ControlPermission.None;
        string? nextControllerId = null;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
                return false;

            if (!session.Clients.TryGetValue(clientId, out client))
                return false;

            if (session.ActiveControllerId != clientId)
                return false;

            oldPermission = client.Permission;
            client.Permission = ControlPermission.None;
            session.ActiveControllerId = null;

            // Process queue for next controller
            while (session.ControlRequestQueue.Count > 0)
            {
                var nextId = session.ControlRequestQueue.Dequeue();
                if (session.Clients.TryGetValue(nextId, out var nextClient) && nextId != clientId)
                {
                    nextControllerId = nextId;
                    nextClient.Permission = ControlPermission.Input;
                    session.ActiveControllerId = nextId;
                    break;
                }
            }
        }

        // Notify permission change for releasing client
        PermissionChanged?.Invoke(this, new PermissionEventArgs
        {
            Session = session,
            ClientId = clientId,
            OldPermission = oldPermission,
            NewPermission = ControlPermission.None
        });

        // Notify permission change for next controller
        if (nextControllerId != null)
        {
            PermissionChanged?.Invoke(this, new PermissionEventArgs
            {
                Session = session,
                ClientId = nextControllerId,
                OldPermission = ControlPermission.None,
                NewPermission = ControlPermission.Input
            });
        }

        return true;
    }

    /// <summary>
    /// Changes a client's role
    /// </summary>
    public bool ChangeRole(Guid sessionId, string clientId, SessionRole newRole)
    {
        SessionInfo? session;
        ClientInfo? client;
        SessionRole oldRole;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
                return false;

            if (!session.Clients.TryGetValue(clientId, out client))
                return false;

            oldRole = client.Role;
            client.Role = newRole;
        }

        RoleChanged?.Invoke(this, new RoleEventArgs
        {
            Session = session,
            ClientId = clientId,
            OldRole = oldRole,
            NewRole = newRole
        });

        return true;
    }

    /// <summary>
    /// Grants permission to a client (host-initiated)
    /// </summary>
    public bool GrantPermission(Guid sessionId, string clientId, ControlPermission permission)
    {
        SessionInfo? session;
        ClientInfo? client;
        ControlPermission oldPermission;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
                return false;

            if (!session.Clients.TryGetValue(clientId, out client))
                return false;

            oldPermission = client.Permission;
            client.Permission = permission;

            if (permission != ControlPermission.None)
            {
                session.ActiveControllerId = clientId;
            }
            else if (session.ActiveControllerId == clientId)
            {
                session.ActiveControllerId = null;
            }
        }

        PermissionChanged?.Invoke(this, new PermissionEventArgs
        {
            Session = session,
            ClientId = clientId,
            OldPermission = oldPermission,
            NewPermission = permission
        });

        return true;
    }

    /// <summary>
    /// Revokes permission from a client
    /// </summary>
    public bool RevokePermission(Guid sessionId, string clientId, string? reason = null)
    {
        return GrantPermission(sessionId, clientId, ControlPermission.None);
    }

    /// <summary>
    /// Gets the client with active control
    /// </summary>
    public ClientInfo? GetActiveController(Guid sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return null;

            if (string.IsNullOrEmpty(session.ActiveControllerId))
                return null;

            return session.Clients.TryGetValue(session.ActiveControllerId, out var client) ? client : null;
        }
    }

    /// <summary>
    /// Checks if a client has control permission
    /// </summary>
    public bool HasControlPermission(Guid sessionId, string clientId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return false;

            if (!session.Clients.TryGetValue(clientId, out var client))
                return false;

            return client.Permission != ControlPermission.None;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            _sessions.Clear();
        }
    }
}
