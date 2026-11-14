using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class RoomCreateObservationGateTests
{
    private static readonly JsonSerializerOptions SceneWireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly string[] Required =
    [
        RoomCreateObservationGate.ContextEvent, RoomCreateObservationGate.ContextRequest,
        RoomCreateObservationGate.SceneEvent, RoomCreateObservationGate.SceneRequest,
    ];

    [Fact]
    public async Task MissingChannelOrBindingDoesNotSubscribeOrRequest()
    {
        var channel = new FakeChannel();
        await using var missingChannel = new RoomCreateObservationGate(null, true, _ => { });
        await using var missingBinding = new RoomCreateObservationGate(channel, false, _ => { });
        Assert.Equal("companion-unavailable", missingChannel.Current.Reason);
        Assert.Equal("binding-unavailable", missingBinding.Current.Reason);
        Assert.False(missingChannel.Current.Active);
        Assert.False(missingBinding.Current.Active);
        Assert.Equal(0, channel.SubscriptionCount);
        Assert.Empty(channel.Requests);
    }

    [Fact]
    public async Task MissingLifecycleSupportRemovesSubscriptionsAndRemainsDormant()
    {
        var channel = new FakeChannel { LifecycleSupported = false };
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        Assert.False(gate.Current.Active);
        Assert.Equal("companion-lifecycle-unavailable", gate.Current.Reason);
        Assert.Equal(0, channel.SubscriptionCount);
        Assert.Empty(channel.Requests);
        Assert.Equal(0, channel.Disposals);
    }

    [Theory]
    [InlineData("game.room.create.context")]
    [InlineData("game.room.create.context.get")]
    [InlineData("game.session.changed")]
    [InlineData("game.session.state")]
    public async Task EveryNegotiatedGrantIsRequired(string missing)
    {
        var channel = new FakeChannel(Required.Where(value => value != missing));
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await channel.EmitContext(Context());
        await channel.EmitScene(Scene());
        Assert.False(gate.Current.Active);
        Assert.Equal("capabilities-unavailable", gate.Current.Reason);
        Assert.Empty(channel.Requests);
    }

    [Fact]
    public async Task PendingBindingAndMissingEpochCannotStartSynchronization()
    {
        var channel = new FakeChannel(state: CompanionChannelState.PendingGame);
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        Assert.Empty(channel.Requests);
        channel.Transition(CompanionChannelState.Connected, null);
        Assert.Empty(channel.Requests);
        channel.Transition(CompanionChannelState.Connected, "epoch-a");
        await Synchronize(channel, gate);
        Assert.True(gate.Current.Active);
        Assert.Equal(2, channel.Requests.Count);
    }

    [Fact]
    public async Task BothFeedsPrecedeSnapshotsAndMainMenuCannotAuthorizeNativeObservation()
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await Synchronize(channel, gate, scene: "main_menu");
        Assert.False(gate.Current.Active);
        Assert.Equal("outside-plaza", gate.Current.Reason);
        Assert.All(channel.Requests, request =>
        {
            Assert.Equal(1, request.Version);
            Assert.Equal("{}", JsonSerializer.Serialize(request.Payload));
            Assert.Equal(new[] { RoomCreateObservationGate.ContextEvent, RoomCreateObservationGate.SceneEvent }, request.Feeds);
            Assert.Null(request.Options);
        });
        await channel.EmitScene(Scene());
        Assert.True(gate.Current.Active);
        Assert.Equal(2, channel.Requests.Count);
    }

    [Theory]
    [InlineData("plaza", true, false)]
    [InlineData("plaza", true, true)]
    [InlineData("main_menu", false, false)]
    [InlineData("main_menu", false, true)]
    public async Task ServerSerializedScenesWithOmittedNullModeWorkForEventsAndSnapshots(string state, bool active, bool snapshot)
    {
        var payload = Scene(state);
        Assert.False(payload.TryGetProperty("mode_id", out _));
        Assert.Equal(state, payload.GetProperty("state").GetString());
        Assert.False(payload.GetProperty("in_match").GetBoolean());
        Assert.False(payload.GetProperty("can_leave_match").GetBoolean());
        Assert.False(payload.GetProperty("is_tutorial").GetBoolean());
        Assert.Equal(4, payload.EnumerateObject().Count());
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await DeliverInitialScene(channel, payload, snapshot);
        await Until(() => gate.Current.Reason == (active ? "plaza-active" : "outside-plaza"));
        Assert.Equal(active, gate.Current.Active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitNullModeStillAuthorizesPlaza(bool snapshot)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            """{"state":"plaza","in_match":false,"can_leave_match":false,"is_tutorial":false,"mode_id":null}""");
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await DeliverInitialScene(channel, payload, snapshot);
        await Until(() => gate.Current.Active);
        Assert.Equal("plaza-active", gate.Current.Reason);
    }

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public async Task MalformedModeIdCannotAuthorizeEventsOrSnapshots(string mode)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            $$"""{"state":"plaza","in_match":false,"can_leave_match":false,"is_tutorial":false,"mode_id":{{mode}}}""");
        var eventChannel = new FakeChannel();
        await using var eventGate = new RoomCreateObservationGate(eventChannel, true, _ => { });
        await Synchronize(eventChannel, eventGate);
        await eventChannel.EmitScene(payload);
        Assert.False(eventGate.Current.Active);
        Assert.Equal("invalid-scene", eventGate.Current.Reason);

        var snapshotChannel = new FakeChannel();
        await using var snapshotGate = new RoomCreateObservationGate(snapshotChannel, true, _ => { });
        await DeliverInitialScene(snapshotChannel, payload, snapshot: true);
        await Until(() => snapshotGate.Current.Reason == "invalid-scene");
        Assert.False(snapshotGate.Current.Active);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("authenticated")]
    [InlineData("main_menu")]
    [InlineData("room_waiting")]
    [InlineData("room_countdown")]
    [InlineData("room_loading")]
    [InlineData("match")]
    [InlineData("result")]
    [InlineData("tutorial")]
    [InlineData("training")]
    [InlineData("migrating")]
    [InlineData("future_scene")]
    public async Task LeavingPlazaRevokesSynchronouslyAndReturningUsesEvents(string scene)
    {
        var channel = new FakeChannel();
        var changes = new List<RoomCreateObservationState>();
        await using var gate = new RoomCreateObservationGate(channel, true, changes.Add);
        await Synchronize(channel, gate);
        var generation = gate.Current.Generation;
        await channel.EmitScene(Scene(scene));
        Assert.False(gate.Current.Active);
        Assert.True(gate.Current.Generation > generation);
        Assert.Equal(gate.Current, changes[^1]);
        await channel.EmitScene(Scene());
        Assert.True(gate.Current.Active);
        Assert.Equal(2, channel.Requests.Count);
    }

    [Fact]
    public async Task ContextRevisionsCannotReopenAnInactiveOrConflictingContext()
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await Synchronize(channel, gate);
        await channel.EmitContext(Context(revision: 2, active: false));
        Assert.False(gate.Current.Active);
        var inactive = gate.Current;
        await channel.EmitContext(Context());
        await channel.EmitContext(Context(revision: 2, active: false));
        Assert.Equal(inactive, gate.Current);
        await channel.EmitContext(Context(revision: 2));
        Assert.False(gate.Current.Active);
        Assert.Equal("conflicting-context-revision", gate.Current.Reason);
        await channel.EmitContext(Context(revision: 3));
        Assert.True(gate.Current.Active);
        Assert.Equal(2, channel.Requests.Count);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"contextId\":\"login-a\",\"revision\":2,\"active\":true}")]
    [InlineData("{\"context_id\":\"\",\"revision\":2,\"active\":true}")]
    [InlineData("{\"context_id\":\"login-a\",\"revision\":0,\"active\":true}")]
    [InlineData("{\"context_id\":\"login-a\",\"revision\":2.5,\"active\":true}")]
    [InlineData("{\"context_id\":\"login-a\",\"revision\":2,\"active\":\"true\"}")]
    [InlineData("{\"context_id\":\"login-a\",\"revision\":2,\"active\":false,\"active\":true}")]
    public async Task MalformedContextRevokesWithoutLoggingItsPayload(string payload)
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await Synchronize(channel, gate);
        await channel.EmitContext(JsonSerializer.Deserialize<JsonElement>(payload));
        Assert.False(gate.Current.Active);
        Assert.Equal("invalid-context", gate.Current.Reason);
    }

    [Theory]
    [InlineData("{\"state\":\"plaza\"}")]
    [InlineData("{\"state\":\"plaza\",\"in_match\":true,\"can_leave_match\":false,\"is_tutorial\":false,\"mode_id\":null}")]
    [InlineData("{\"state\":\"plaza\",\"in_match\":false,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":null}")]
    [InlineData("{\"state\":\"plaza\",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":true,\"mode_id\":null}")]
    [InlineData("{\"state\":\"plaza\",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":false,\"mode_id\":1}")]
    [InlineData("{\"state\":\"main_menu\",\"state\":\"plaza\",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":false,\"mode_id\":null}")]
    public async Task MalformedOrContradictorySceneRevokes(string payload)
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await Synchronize(channel, gate);
        await channel.EmitScene(JsonSerializer.Deserialize<JsonElement>(payload));
        Assert.False(gate.Current.Active);
        Assert.Equal("invalid-scene", gate.Current.Reason);
    }

    [Fact]
    public async Task EventsReceivedDuringEitherSnapshotTakePrecedence()
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        var context = await channel.NextRequest();
        var contextSnapshot = context.Receive(Context());
        await channel.EmitContext(Context(revision: 2, active: false));
        context.Complete(contextSnapshot);
        var scene = await channel.NextRequest();
        var sceneSnapshot = scene.Receive(Scene());
        await channel.EmitScene(Scene("main_menu"));
        scene.Complete(sceneSnapshot);
        await Until(() => channel.CompletedRequests == 2);
        Assert.False(gate.Current.Active);
        await channel.EmitContext(Context(revision: 3));
        Assert.False(gate.Current.Active);
        await channel.EmitScene(Scene());
        Assert.True(gate.Current.Active);
        Assert.Equal(2, channel.Requests.Count);
    }

    [Fact]
    public async Task NewerSnapshotsTakePrecedenceOverEarlierEventsDeliveredWhileRequestsWait()
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        var context = await channel.NextRequest();
        await channel.EmitContext(Context(active: false));
        context.Complete(Context(revision: 2));
        var scene = await channel.NextRequest();
        await channel.EmitScene(Scene());
        scene.Complete(Scene("main_menu"));
        await Until(() => gate.Current.Reason == "outside-plaza");
        Assert.False(gate.Current.Active);
        await channel.EmitScene(Scene());
        Assert.True(gate.Current.Active);
        Assert.Equal(2, channel.Requests.Count);
    }

    [Fact]
    public async Task DelayedEventsCannotRestoreAnOlderLoginOrPlazaAfterNewerSnapshots()
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        var context = await channel.NextRequest();
        var oldContext = channel.ReceiveEvent(RoomCreateObservationGate.ContextEvent, Context("retired-login"));
        context.Complete(Context());
        var scene = await channel.NextRequest();
        var oldScene = channel.ReceiveEvent(RoomCreateObservationGate.SceneEvent, Scene());
        scene.Complete(Scene("main_menu"));
        await Until(() => gate.Current.Reason == "outside-plaza");
        var current = gate.Current;
        await channel.DeliverEvent(oldContext);
        await channel.DeliverEvent(oldScene);
        Assert.Equal(current, gate.Current);
        Assert.False(gate.Current.Active);
        Assert.Equal(2, channel.Requests.Count);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("epoch-a", 0)]
    [InlineData("retired-epoch", 999)]
    public async Task SnapshotsWithoutCurrentReceiveIdentityCannotAuthorize(string? epoch, long sequence)
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        (await channel.NextRequest()).Complete(new CompanionOperationResult(CompanionOperationStatus.Completed, Response: Context())
        { ConnectionEpoch = epoch, ReceiveSequence = sequence });
        (await channel.NextRequest()).Complete(new CompanionOperationResult(CompanionOperationStatus.Completed, Response: Scene())
        { ConnectionEpoch = epoch, ReceiveSequence = sequence });
        await Until(() => gate.Current.Reason == "scene-snapshot-unavailable");
        Assert.False(gate.Current.Active);
        await channel.EmitContext(Context());
        await channel.EmitScene(Scene());
        Assert.True(gate.Current.Active);
    }

    [Theory]
    [InlineData("game.room.create.context", "context-sequence-unavailable")]
    [InlineData("game.session.changed", "scene-sequence-unavailable")]
    public async Task UnstampedEventsCannotPreserveAuthorization(string type, string reason)
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await Synchronize(channel, gate);
        var message = channel.ReceiveEvent(type, type == RoomCreateObservationGate.ContextEvent ? Context() : Scene());
        await channel.DeliverEvent(message with { ReceiveSequence = 0 });
        Assert.False(gate.Current.Active);
        Assert.Equal(reason, gate.Current.Reason);
    }

    [Fact]
    public async Task DisconnectRevokesAndCancelsTheCurrentRequestBeforeItsReply()
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        var request = await channel.NextRequest();
        await channel.EmitContext(Context());
        await channel.EmitScene(Scene());
        Assert.True(gate.Current.Active);
        channel.Transition(CompanionChannelState.Reconnecting, "epoch-a");
        Assert.False(gate.Current.Active);
        Assert.True(request.Token.IsCancellationRequested);
        request.Complete(Context());
        await channel.EmitContext(Context());
        await channel.EmitScene(Scene());
        Assert.False(gate.Current.Active);
        Assert.Single(channel.Requests);
    }

    [Fact]
    public async Task ReconnectRejectsOldEpochEventsLateRepliesAndStaleLifecycleCallbacks()
    {
        var channel = new FakeChannel { HonorCancellation = false };
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        var oldRequest = await channel.NextRequest();
        var oldLifecycle = channel.ConnectionSnapshot;
        await channel.EmitContext(Context());
        await channel.EmitScene(Scene());
        channel.Transition(CompanionChannelState.Reconnecting, "epoch-a");
        channel.Transition(CompanionChannelState.Connected, "epoch-b");
        channel.DeliverLifecycle(oldLifecycle);
        await channel.EmitContext(Context(), "epoch-a");
        await channel.EmitScene(Scene(), "epoch-a");
        Assert.False(gate.Current.Active);
        oldRequest.Complete(Context());
        await Synchronize(channel, gate, id: "login-b");
        Assert.True(gate.Current.Active);
        await channel.EmitContext(Context(active: false), "epoch-a");
        await channel.EmitScene(Scene("match"), "epoch-a");
        Assert.True(gate.Current.Active);
        channel.Transition(CompanionChannelState.Connected, "epoch-b");
        Assert.Equal(3, channel.Requests.Count);
    }

    [Fact]
    public async Task ReplacingContextDiscardsOldPlazaAndRequiresFreshSceneSynchronization()
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await Synchronize(channel, gate);
        await channel.EmitContext(Context("login-b"));
        Assert.False(gate.Current.Active);
        await channel.EmitScene(Scene());
        Assert.False(gate.Current.Active);
        var context = await channel.NextRequest();
        Assert.Equal(RoomCreateObservationGate.ContextRequest, context.Type);
        context.Complete(Context("login-b"));
        var scene = await channel.NextRequest();
        scene.Complete(Scene());
        await Until(() => gate.Current.Active);
        Assert.Equal(4, channel.Requests.Count);
        await channel.EmitContext(Context("login-b"));
        Assert.Equal(4, channel.Requests.Count);
    }

    [Fact]
    public async Task NewerSceneEventWinsWhileReplacementSnapshotEstablishesFreshness()
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await Synchronize(channel, gate);
        await channel.EmitContext(Context("login-b"));
        (await channel.NextRequest()).Complete(Context("login-b"));
        var scene = await channel.NextRequest();
        var snapshot = scene.Receive(Scene());
        await channel.EmitScene(Scene("room_waiting"));
        scene.Complete(snapshot);
        await Until(() => gate.Current.Reason == "outside-plaza");
        Assert.False(gate.Current.Active);
        await channel.EmitScene(Scene());
        Assert.True(gate.Current.Active);
        Assert.Equal(4, channel.Requests.Count);
    }

    [Fact]
    public async Task SceneReplyReceivedBeforeReplacementCannotSatisfyItsFreshnessBarrier()
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        (await channel.NextRequest()).Complete(Context());
        var initialScene = await channel.NextRequest();
        var oldSnapshot = initialScene.Receive(Scene());
        initialScene.Complete(oldSnapshot);
        await Until(() => gate.Current.Active);
        await channel.EmitContext(Context("login-b"));
        (await channel.NextRequest()).Complete(Context("login-b"));
        (await channel.NextRequest()).Complete(oldSnapshot);
        await Until(() => gate.Current.Reason == "scene-snapshot-unavailable");
        Assert.False(gate.Current.Active);
        Assert.Equal(4, channel.Requests.Count);
    }

    [Fact]
    public async Task ReplacementCancelsAnOldSceneRequestAndCoalescesFurtherLoginChanges()
    {
        var channel = new FakeChannel { HonorCancellation = false };
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        (await channel.NextRequest()).Complete(Context());
        var oldScene = await channel.NextRequest();
        await channel.EmitScene(Scene());
        Assert.True(gate.Current.Active);
        await channel.EmitContext(Context("login-b"));
        await channel.EmitContext(Context("login-c"));
        Assert.False(gate.Current.Active);
        Assert.True(oldScene.Token.IsCancellationRequested);
        oldScene.Complete(Scene());
        var context = await channel.NextRequest();
        Assert.Equal(RoomCreateObservationGate.ContextRequest, context.Type);
        Assert.False(gate.Current.Active);
        context.Complete(Context("login-c"));
        (await channel.NextRequest()).Complete(Scene());
        await Until(() => gate.Current.Active);
        Assert.Equal(4, channel.Requests.Count);
    }

    [Theory]
    [InlineData(CompanionOperationStatus.Busy)]
    [InlineData(CompanionOperationStatus.Rejected)]
    [InlineData(CompanionOperationStatus.Canceled)]
    [InlineData(CompanionOperationStatus.Unavailable)]
    public async Task UnavailableSnapshotsDoNotRetryAndInitialEventsCanSupplyState(CompanionOperationStatus status)
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        (await channel.NextRequest()).Complete(status);
        (await channel.NextRequest()).Complete(status);
        await Until(() => gate.Current.Reason == "scene-snapshot-unavailable");
        Assert.False(gate.Current.Active);
        await channel.EmitContext(Context());
        await channel.EmitScene(Scene());
        Assert.True(gate.Current.Active);
        for (var index = 0; index < 20; index++)
        {
            await channel.EmitContext(Context());
            await channel.EmitScene(Scene());
            channel.Transition(CompanionChannelState.Connected, "epoch-a");
        }
        Assert.Equal(2, channel.Requests.Count);
    }

    [Theory]
    [InlineData(CompanionOperationStatus.Busy)]
    [InlineData(CompanionOperationStatus.Rejected)]
    [InlineData(CompanionOperationStatus.Canceled)]
    public async Task LaterPlazaEventResynchronizesFailedReplacementWithoutAuthorizingFromTheEvent(CompanionOperationStatus status)
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        await Synchronize(channel, gate);
        await channel.EmitContext(Context("login-b", revision: 3));
        (await channel.NextRequest()).Complete(Context("login-b", revision: 3));
        var scene = await channel.NextRequest();
        await channel.EmitScene(Scene());
        scene.Complete(status);
        await Until(() => gate.Current.Reason == "scene-snapshot-unavailable");
        Assert.False(gate.Current.Active);

        await channel.EmitContext(Context("login-b", revision: 2));
        await channel.EmitContext(Context("login-b", revision: 3));
        await channel.EmitScene(Scene(), "retired-epoch");
        await channel.EmitScene(Scene("room_waiting"));
        Assert.Equal(4, channel.Requests.Count);
        await channel.EmitContext(Context("login-b", revision: 4, active: false));
        await channel.EmitScene(Scene());
        Assert.False(gate.Current.Active);
        Assert.Equal(4, channel.Requests.Count);
        await channel.EmitContext(Context("login-b", revision: 5));
        Assert.Equal(4, channel.Requests.Count);

        await channel.EmitScene(Scene());
        Assert.False(gate.Current.Active);
        var retryContext = await channel.NextRequest();
        Assert.Equal(RoomCreateObservationGate.ContextRequest, retryContext.Type);
        await channel.EmitScene(Scene());
        await channel.EmitScene(Scene());
        Assert.Equal(5, channel.Requests.Count);
        retryContext.Complete(Context("login-b", revision: 5));
        var retryScene = await channel.NextRequest();
        Assert.Equal(RoomCreateObservationGate.SceneRequest, retryScene.Type);
        await channel.EmitScene(Scene());
        Assert.False(gate.Current.Active);
        Assert.Equal(6, channel.Requests.Count);
        retryScene.Complete(Scene());
        await Until(() => gate.Current.Active);
        await channel.EmitScene(Scene());
        Assert.Equal(6, channel.Requests.Count);
    }

    [Fact]
    public async Task SnapshotFailureStillAllowsTheOtherSnapshotAndDoesNotRetry()
    {
        var channel = new FakeChannel();
        await using var gate = new RoomCreateObservationGate(channel, true, _ => { });
        (await channel.NextRequest()).Fail(new IOException("private diagnostic"));
        var scene = await channel.NextRequest();
        var generation = gate.Current.Generation;
        scene.Complete(Scene());
        await Until(() => gate.Current.Generation > generation && gate.Current.Reason == "waiting-context");
        Assert.False(gate.Current.Active);
        await channel.EmitContext(Context());
        Assert.True(gate.Current.Active);
        Assert.Equal(2, channel.Requests.Count);
    }

    [Fact]
    public async Task DisposalRevokesUnsubscribesAndCancelsWithoutDisposingSharedChannel()
    {
        var channel = new FakeChannel();
        var changes = new ConcurrentQueue<RoomCreateObservationState>();
        var gate = new RoomCreateObservationGate(channel, true, changes.Enqueue);
        var request = await channel.NextRequest();
        var oldContextCallback = channel.EventCallback(RoomCreateObservationGate.ContextEvent);
        await channel.EmitContext(Context());
        await channel.EmitScene(Scene());
        Assert.True(gate.Current.Active);
        await gate.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(gate.Current.Active);
        Assert.Equal("closed", gate.Current.Reason);
        Assert.True(request.Token.IsCancellationRequested);
        Assert.Equal(0, channel.SubscriptionCount);
        Assert.Equal(0, channel.Disposals);
        var count = changes.Count;
        await oldContextCallback(new(RoomCreateObservationGate.ContextEvent, 1, "late", Context()) { ConnectionEpoch = "epoch-a" });
        await gate.DisposeAsync();
        Assert.Equal(count, changes.Count);
    }

    private static object Context(string id = "login-a", long revision = 1, bool active = true) =>
        new { context_id = id, revision, active };

    private static JsonElement Scene(string state = "plaza") => JsonSerializer.SerializeToElement(
        new { State = state, InMatch = false, CanLeaveMatch = false, IsTutorial = false, ModeId = (int?)null }, SceneWireOptions);

    private static async Task DeliverInitialScene(FakeChannel channel, JsonElement scene, bool snapshot)
    {
        var contextRequest = await channel.NextRequest();
        if (snapshot)
        {
            contextRequest.Complete(Context());
            (await channel.NextRequest()).Complete(scene);
        }
        else
        {
            await channel.EmitContext(Context());
            await channel.EmitScene(scene);
        }
    }

    private static async Task Synchronize(FakeChannel channel, RoomCreateObservationGate gate, string id = "login-a", string scene = "plaza")
    {
        var contextRequest = await channel.NextRequest();
        Assert.Equal(RoomCreateObservationGate.ContextRequest, contextRequest.Type);
        contextRequest.Complete(Context(id));
        var sceneRequest = await channel.NextRequest();
        Assert.Equal(RoomCreateObservationGate.SceneRequest, sceneRequest.Type);
        sceneRequest.Complete(Scene(scene));
        await Until(() => gate.Current.Reason == (scene == "plaza" ? "plaza-active" : "outside-plaza"));
    }

    private static async Task Until(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate() && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Assert.True(predicate(), "The expected observation state did not arrive before the deadline.");
    }

    private sealed class FakeChannel : ICompanionChannel
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Func<CompanionEvent, ValueTask>> _feeds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _sequences = new(StringComparer.Ordinal);
        private readonly List<Action<CompanionConnectionSnapshot>> _observers = [];
        private readonly Channel<PendingRequest> _requests = Channel.CreateUnbounded<PendingRequest>();
        private CompanionConnectionSnapshot _snapshot;
        private int _completedRequests;

        internal FakeChannel(IEnumerable<string>? capabilities = null, CompanionChannelState state = CompanionChannelState.Connected)
        {
            _snapshot = new("epoch-a", state, (capabilities ?? Required).ToHashSet(StringComparer.Ordinal));
        }

        internal bool LifecycleSupported { get; init; } = true;
        internal bool HonorCancellation { get; init; } = true;
        internal int Disposals { get; private set; }
        internal int CompletedRequests => Volatile.Read(ref _completedRequests);
        internal ConcurrentQueue<PendingRequest> Requests { get; } = new();
        internal int SubscriptionCount { get { lock (_gate) return _feeds.Count + _observers.Count; } }
        public CompanionChannelState State => ConnectionSnapshot.State;
        public IReadOnlySet<string> Capabilities => ConnectionSnapshot.Capabilities;
        public CompanionConnectionSnapshot ConnectionSnapshot { get { lock (_gate) return _snapshot; } }

        internal void Transition(CompanionChannelState state, string? epoch)
        {
            CompanionConnectionSnapshot snapshot;
            lock (_gate) snapshot = _snapshot = _snapshot with { State = state, Epoch = epoch, Revision = _snapshot.Revision + 1 };
            DeliverLifecycle(snapshot);
        }

        internal void DeliverLifecycle(CompanionConnectionSnapshot snapshot)
        {
            Action<CompanionConnectionSnapshot>[] observers;
            lock (_gate) observers = _observers.ToArray();
            foreach (var observer in observers) observer(snapshot);
        }

        internal Func<CompanionEvent, ValueTask> EventCallback(string type) { lock (_gate) return _feeds[type]; }
        internal ValueTask EmitContext(object payload, string? epoch = null) => Emit(RoomCreateObservationGate.ContextEvent, payload, epoch);
        internal ValueTask EmitScene(object payload, string? epoch = null) => Emit(RoomCreateObservationGate.SceneEvent, payload, epoch);
        private ValueTask Emit(string type, object payload, string? epoch) => DeliverEvent(ReceiveEvent(type, payload, epoch));

        internal CompanionEvent ReceiveEvent(string type, object payload, string? epoch = null)
        {
            epoch ??= ConnectionSnapshot.Epoch;
            return new(type, 1, "event", payload) { ConnectionEpoch = epoch, ReceiveSequence = NextSequence(epoch) };
        }

        internal ValueTask DeliverEvent(CompanionEvent message)
        {
            Func<CompanionEvent, ValueTask>? callback;
            lock (_gate) _feeds.TryGetValue(message.Type, out callback);
            return callback?.Invoke(message) ?? ValueTask.CompletedTask;
        }

        private long NextSequence(string? epoch)
        {
            lock (_gate)
            {
                var key = epoch ?? "";
                var sequence = _sequences.GetValueOrDefault(key) + 1;
                _sequences[key] = sequence;
                return sequence;
            }
        }

        internal async Task<PendingRequest> NextRequest() => await _requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public IDisposable SubscribeState(Action<CompanionConnectionSnapshot> handler)
        {
            if (!LifecycleSupported) throw new NotSupportedException();
            lock (_gate) _observers.Add(handler);
            handler(ConnectionSnapshot);
            return new Subscription(() => { lock (_gate) _observers.Remove(handler); });
        }

        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, ValueTask> handler)
        {
            Assert.Equal(1, version);
            lock (_gate) _feeds.Add(type, handler);
            return new Subscription(() => { lock (_gate) _feeds.Remove(type); });
        }

        public ValueTask<CompanionOperationResult> SendAsync(string type, int version, object? payload = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The observation gate must not send events.");

        public async ValueTask<CompanionOperationResult> RequestAsync(string type, int version, object? payload = null,
            CompanionRequestOptions? options = null, CancellationToken cancellationToken = default)
        {
            string[] feeds;
            lock (_gate) feeds = _feeds.Keys.Order(StringComparer.Ordinal).ToArray();
            var epoch = ConnectionSnapshot.Epoch;
            var request = new PendingRequest(type, version, payload, options, feeds, epoch, () => NextSequence(epoch), cancellationToken);
            Requests.Enqueue(request);
            _requests.Writer.TryWrite(request);
            try
            {
                return HonorCancellation ? await request.Result.WaitAsync(cancellationToken)
                    : await request.Result.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            finally { Interlocked.Increment(ref _completedRequests); }
        }

        public void Dispose() => Disposals++;
    }

    private sealed class PendingRequest(string type, int version, object? payload, CompanionRequestOptions? options,
        string[] feeds, string? epoch, Func<long> nextSequence, CancellationToken token)
    {
        private readonly TaskCompletionSource<CompanionOperationResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Type { get; } = type;
        internal int Version { get; } = version;
        internal object? Payload { get; } = payload;
        internal CompanionRequestOptions? Options { get; } = options;
        internal CancellationToken Token { get; } = token;
        internal string[] Feeds { get; } = feeds;
        internal Task<CompanionOperationResult> Result => _result.Task;
        internal CompanionOperationResult Receive(object payload) => new(CompanionOperationStatus.Completed, Response: payload)
        { ConnectionEpoch = epoch, ReceiveSequence = nextSequence() };
        internal void Complete(object payload) => Complete(Receive(payload));
        internal void Complete(CompanionOperationResult result) => _result.TrySetResult(result);
        internal void Complete(CompanionOperationStatus status) => Complete(status == CompanionOperationStatus.Rejected
            ? new CompanionOperationResult(status) { ConnectionEpoch = epoch, ReceiveSequence = nextSequence() }
            : new CompanionOperationResult(status));
        internal void Fail(Exception error) => _result.TrySetException(error);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
