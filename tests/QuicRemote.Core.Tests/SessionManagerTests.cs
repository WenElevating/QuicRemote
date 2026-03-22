using System.Collections.Concurrent;
using QuicRemote.Core.Session;
using Xunit;

namespace QuicRemote.Core.Tests;

public class SessionManagerTests
{
    [Fact]
    public void CreateSession_ReturnsValidSession()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-001");

        Assert.NotEqual(Guid.Empty, session.SessionId);
        Assert.Equal("device-001", session.DeviceId);
        Assert.Equal(SessionState.Idle, session.State);
    }

    [Fact]
    public void GetSession_ReturnsCreatedSession()
    {
        using var manager = new SessionManager();
        var created = manager.CreateSession("device-002");
        var retrieved = manager.GetSession(created.SessionId);

        Assert.NotNull(retrieved);
        Assert.Equal(created.SessionId, retrieved.SessionId);
    }

    [Fact]
    public void GetSession_WithInvalidId_ReturnsNull()
    {
        using var manager = new SessionManager();
        var result = manager.GetSession(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public void UpdateState_ChangesStateAndRaisesEvent()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-003");

        SessionInfo? eventSession = null;
        manager.SessionStateChanged += (s, e) => eventSession = e.Session;

        manager.UpdateState(session.SessionId, SessionState.Connected);

        Assert.Equal(SessionState.Connected, session.State);
        Assert.NotNull(eventSession);
        Assert.Equal(session.SessionId, eventSession.SessionId);
    }

    [Fact]
    public void RemoveSession_RemovesAndRaisesEvent()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-004");

        SessionInfo? eventSession = null;
        manager.SessionEnded += (s, e) => eventSession = e.Session;

        manager.RemoveSession(session.SessionId);

        Assert.Null(manager.GetSession(session.SessionId));
        Assert.NotNull(eventSession);
    }

    [Fact]
    public void GetActiveSessions_ReturnsOnlyActiveSessions()
    {
        using var manager = new SessionManager();

        var activeSession = manager.CreateSession("device-active");
        var idleSession = manager.CreateSession("device-idle");

        manager.UpdateState(activeSession.SessionId, SessionState.Active);

        var activeSessions = manager.GetActiveSessions();

        Assert.Single(activeSessions);
        Assert.Equal(activeSession.SessionId, activeSessions[0].SessionId);
    }

    // Client management tests

    [Fact]
    public void AddClient_AddsClientToSession()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-005");

        var result = manager.AddClient(session.SessionId, "client-001", SessionRole.Viewer);

        Assert.True(result);
        Assert.Single(session.Clients);
        Assert.True(session.Clients.ContainsKey("client-001"));
    }

    [Fact]
    public void AddClient_SetsCorrectDefaultPermission()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-006");

        manager.AddClient(session.SessionId, "viewer", SessionRole.Viewer);
        manager.AddClient(session.SessionId, "controller", SessionRole.Controller);

        Assert.Equal(ControlPermission.None, session.Clients["viewer"].Permission);
        Assert.Equal(ControlPermission.Input, session.Clients["controller"].Permission);
    }

    [Fact]
    public void AddClient_DuplicateClient_ReturnsFalse()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-007");

        manager.AddClient(session.SessionId, "client-001");
        var result = manager.AddClient(session.SessionId, "client-001");

        Assert.False(result);
        Assert.Single(session.Clients);
    }

    [Fact]
    public void RemoveClient_RemovesClientFromSession()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-008");

        manager.AddClient(session.SessionId, "client-001");
        var result = manager.RemoveClient(session.SessionId, "client-001");

        Assert.True(result);
        Assert.Empty(session.Clients);
    }

    // Control permission tests

    [Fact]
    public void RequestControl_NoActiveController_GrantsImmediately()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-009");
        manager.AddClient(session.SessionId, "client-001");

        var result = manager.RequestControl(session.SessionId, "client-001", ControlPermission.Input);

        Assert.True(result);
        Assert.Equal(ControlPermission.Input, session.Clients["client-001"].Permission);
        Assert.Equal("client-001", session.ActiveControllerId);
    }

    [Fact]
    public void RequestControl_AnotherClientHasControl_QueuesRequest()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-010");
        manager.AddClient(session.SessionId, "client-001");
        manager.AddClient(session.SessionId, "client-002");

        // First client gets control
        manager.RequestControl(session.SessionId, "client-001", ControlPermission.Input);

        // Second client requests control
        var result = manager.RequestControl(session.SessionId, "client-002", ControlPermission.Input);

        Assert.False(result); // Queued, not granted
        Assert.Equal("client-001", session.ActiveControllerId);
        Assert.Contains("client-002", session.ControlRequestQueue);
    }

    [Fact]
    public void ReleaseControl_ReleasesAndGrantsToNextInQueue()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-011");
        manager.AddClient(session.SessionId, "client-001");
        manager.AddClient(session.SessionId, "client-002");

        manager.RequestControl(session.SessionId, "client-001", ControlPermission.Input);
        manager.RequestControl(session.SessionId, "client-002", ControlPermission.Input);

        // Release from first client
        var result = manager.ReleaseControl(session.SessionId, "client-001");

        Assert.True(result);
        Assert.Equal(ControlPermission.None, session.Clients["client-001"].Permission);
        Assert.Equal("client-002", session.ActiveControllerId);
        Assert.Equal(ControlPermission.Input, session.Clients["client-002"].Permission);
    }

    [Fact]
    public void ReleaseControl_ByNonController_ReturnsFalse()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-012");
        manager.AddClient(session.SessionId, "client-001");
        manager.AddClient(session.SessionId, "client-002");

        manager.RequestControl(session.SessionId, "client-001", ControlPermission.Input);

        var result = manager.ReleaseControl(session.SessionId, "client-002");

        Assert.False(result);
        Assert.Equal("client-001", session.ActiveControllerId);
    }

    // Grant/Revoke permission tests

    [Fact]
    public void GrantPermission_SetsPermissionAndActiveController()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-013");
        manager.AddClient(session.SessionId, "client-001");

        var result = manager.GrantPermission(session.SessionId, "client-001", ControlPermission.Full);

        Assert.True(result);
        Assert.Equal(ControlPermission.Full, session.Clients["client-001"].Permission);
        Assert.Equal("client-001", session.ActiveControllerId);
    }

    [Fact]
    public void RevokePermission_ClearsPermissionAndActiveController()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-014");
        manager.AddClient(session.SessionId, "client-001");

        manager.GrantPermission(session.SessionId, "client-001", ControlPermission.Input);
        var result = manager.RevokePermission(session.SessionId, "client-001");

        Assert.True(result);
        Assert.Equal(ControlPermission.None, session.Clients["client-001"].Permission);
        Assert.Null(session.ActiveControllerId);
    }

    // Event tests

    [Fact]
    public void PermissionChanged_RaisedOnGrant()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-015");
        manager.AddClient(session.SessionId, "client-001");

        PermissionEventArgs? eventArgs = null;
        manager.PermissionChanged += (s, e) => eventArgs = e;

        manager.GrantPermission(session.SessionId, "client-001", ControlPermission.Input);

        Assert.NotNull(eventArgs);
        Assert.Equal("client-001", eventArgs.ClientId);
        Assert.Equal(ControlPermission.None, eventArgs.OldPermission);
        Assert.Equal(ControlPermission.Input, eventArgs.NewPermission);
    }

    [Fact]
    public void RoleChanged_RaisedOnRoleChange()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-016");
        manager.AddClient(session.SessionId, "client-001", SessionRole.Viewer);

        RoleEventArgs? eventArgs = null;
        manager.RoleChanged += (s, e) => eventArgs = e;

        manager.ChangeRole(session.SessionId, "client-001", SessionRole.Controller);

        Assert.NotNull(eventArgs);
        Assert.Equal("client-001", eventArgs.ClientId);
        Assert.Equal(SessionRole.Viewer, eventArgs.OldRole);
        Assert.Equal(SessionRole.Controller, eventArgs.NewRole);
    }

    [Fact]
    public void SessionStarted_RaisedWhenStateChangesToActive()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-017");

        SessionEventArgs? eventArgs = null;
        manager.SessionStarted += (s, e) => eventArgs = e;

        manager.UpdateState(session.SessionId, SessionState.Active);

        Assert.NotNull(eventArgs);
        Assert.Equal(session.SessionId, eventArgs.Session.SessionId);
    }

    // HasControlPermission test

    [Fact]
    public void HasControlPermission_ReturnsCorrectValue()
    {
        using var manager = new SessionManager();
        var session = manager.CreateSession("device-018");
        manager.AddClient(session.SessionId, "client-001");

        Assert.False(manager.HasControlPermission(session.SessionId, "client-001"));

        manager.GrantPermission(session.SessionId, "client-001", ControlPermission.Input);

        Assert.True(manager.HasControlPermission(session.SessionId, "client-001"));
    }

    // Thread safety test

    [Fact]
    public async Task SessionManager_ThreadSafe()
    {
        using var manager = new SessionManager();
        var sessionIds = new ConcurrentBag<Guid>();

        // Create sessions from multiple threads
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            var session = manager.CreateSession($"device-{i}");
            sessionIds.Add(session.SessionId);
        }));

        await Task.WhenAll(tasks);

        Assert.Equal(100, sessionIds.Count);
    }
}
