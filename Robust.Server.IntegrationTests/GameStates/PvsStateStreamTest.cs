using System;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Robust.Server.GameStates;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Serilog.Events;

namespace Robust.UnitTesting.Server.GameStates;

/// <summary>
/// PVS serializes every game state into a pooled stream, and <c>RobustMemoryManager</c> treats a pooled stream that
/// reaches its finalizer as a leak: it throws "Stream finalized but not disposed" on the finalizer thread, which takes
/// down the whole server. So the stream has to be released on every path, including the ones that never send it.
/// </summary>
public sealed class PvsStateStreamTest : RobustIntegrationTest
{
    /// <summary>
    /// A server-side disconnect (e.g. a console kick) leaves a real channel "Disconnecting" for a tick or so: the channel
    /// already reports that it is not connected, but its session is still in game, so PVS still serializes a game state
    /// for it.
    /// </summary>
    [Test]
    public async Task DisconnectingChannelReleasesStream()
    {
        var server = StartServer();
        var client = StartClient();

        await Task.WhenAll(client.WaitIdleAsync(), server.WaitIdleAsync());

        var netMan = client.ResolveDependency<IClientNetManager>();
        var pvs = server.System<PvsSystem>();

        Assert.DoesNotThrow(() => client.SetConnectTarget(server));
        client.Post(() => netMan.ClientConnect(null!, 0, null!));
        await RunTicks();

        // Put the player in game, so that PVS sends them states.
        ICommonSession session = default!;
        await server.WaitPost(() =>
        {
            var map = server.System<SharedMapSystem>().CreateMap();
            var player = server.EntMan.SpawnEntity(null, new EntityCoordinates(map, default));
            session = server.PlayerMan.Sessions.Single();
            server.PlayerMan.SetAttachedEntity(session, player);
            server.PlayerMan.JoinGame(session);
        });
        await RunTicks();
        Assert.That(session.Status, Is.EqualTo(SessionStatus.InGame));

        // One tick in the "Disconnecting" window.
        await server.WaitPost(() => SetConnected(session.Channel, false));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(session.Status, Is.EqualTo(SessionStatus.InGame));
            Assert.That(pvs.PlayerData[session].StateStream, Is.Null, "The game state stream was not released.");
        });

        // Let the kick finish. The integration net manager flips IsConnected itself once it drops the channel.
        await server.WaitPost(() =>
        {
            SetConnected(session.Channel, true);
            session.Channel.Disconnect("Kicked by test");
        });
        await RunTicks();

        await server.WaitAssertion(() =>
        {
            Assert.That(session.Status, Is.EqualTo(SessionStatus.Disconnected));
            Assert.That(pvs.PlayerData.ContainsKey(session), Is.False);
        });
        Assert.That(netMan.IsConnected, Is.False);

        async Task RunTicks()
        {
            for (var i = 0; i < 10; i++)
            {
                await server.WaitRunTicks(1);
                await client.WaitRunTicks(1);
            }
        }
    }

    /// <summary>
    /// PVS catches and logs whatever sending a session's state throws; the stream still has to be released.
    /// </summary>
    [Test]
    public async Task ThrowingSendReleasesStream()
    {
        // The failed send gets logged as an error, and that is exactly the path under test.
        var server = StartServer(new ServerIntegrationOptions { FailureLogLevel = LogLevel.Fatal });
        await server.WaitIdleAsync();

        var pvs = server.System<PvsSystem>();
        var catcher = new LogCatcher();
        server.ResolveDependency<ILogManager>().GetSawmill("system.pvs").AddHandler(catcher);

        // The integration net manager can only send to its own channels, and throws on anything else.
        var channel = new Mock<INetChannel>();
        channel.SetupGet(x => x.IsConnected).Returns(true);
        channel.SetupGet(x => x.UserId).Returns(new NetUserId(Guid.NewGuid()));
        channel.SetupGet(x => x.UserName).Returns("foreign");

        ICommonSession session = default!;
        await server.WaitPost(() =>
        {
            session = server.PlayerMan.CreateAndAddSession(channel.Object);
            server.PlayerMan.JoinGame(session);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(catcher.CaughtLogs.Any(x => x.Level == LogEventLevel.Error && x.Exception is InvalidCastException),
                "Sending the state did not throw, so this test no longer covers anything.");
            Assert.That(pvs.PlayerData[session].StateStream, Is.Null, "The game state stream was not released.");
        });
    }

    /// <summary>
    /// The integration net manager only flips <see cref="INetChannel.IsConnected"/> together with dropping the session,
    /// so the window that a real channel spends in "Disconnecting" has to be faked.
    /// </summary>
    private static void SetConnected(INetChannel channel, bool connected)
    {
        var property = channel.GetType().GetProperty(nameof(INetChannel.IsConnected));
        Assert.That(property?.CanWrite, Is.True, "The integration net channel no longer lets the test fake a disconnecting channel.");
        property!.SetValue(channel, connected);
    }
}
