using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using VAL.App.Host.Commands;
using VAL.Continuum;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host;
using VAL.Host.Abyss;
using VAL.Host.Commands;
using VAL.Host.Services;
using VAL.Tests.Shared;

using Xunit;

namespace VAL.Tests.Abyss;

public sealed class AbyssRuntimeIntegrationTests
{
    [Fact]
    public async Task LatestSearchPublishesMatchingQueryResultsAndFileWhenUiDeliveryIsReordered()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Search(fixture.ChatId, "alpha", 4, "original alpha", requestId: "older");
        var older = await fixture.Ui.TakeNextAsync();
        fixture.Runtime.Search(fixture.ChatId, "beta", 4, "original beta", requestId: "newer");
        var newer = await fixture.Ui.TakeNextAsync();
        newer();
        older();

        var message = Assert.Single(fixture.Sender.Messages);
        Assert.Equal(fixture.ChatId, message.ChatId);
        var payload = message.Payload!.Value;
        Assert.Equal("newer", payload.GetProperty("requestId").GetString());
        Assert.False(payload.GetProperty("cleared").GetBoolean());
        Assert.Equal("beta", payload.GetProperty("queryUsed").GetString());
        Assert.Equal("original beta", payload.GetProperty("queryOriginal").GetString());
        Assert.Contains("beta", payload.GetProperty("results")[0].GetProperty("snippet").GetString());
        var file = Path.Combine(fixture.Paths.MemoryChatsRoot, fixture.ChatId, "Abyss.Results.txt");
        Assert.Contains("Query: beta", File.ReadAllText(file));
        Assert.DoesNotContain("Query: alpha", File.ReadAllText(file));
        await fixture.Tasks.StopAsync(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearInvalidatesSearchAndRecentRecallBeforeUiPublication(bool recent)
    {
        using var fixture = new Fixture();
        if (recent) fixture.Runtime.FetchLast(fixture.ChatId, 1, inject: true);
        else fixture.Runtime.Search(fixture.ChatId, "alpha", 4);
        var delayed = await fixture.Ui.TakeNextAsync();
        fixture.Runtime.ClearResults(fixture.ChatId, "clear-request");
        delayed();

        var message = Assert.Single(fixture.Sender.Messages);
        Assert.Equal("abyss.results", message.Name);
        Assert.Equal("clear-request", message.Payload!.Value.GetProperty("requestId").GetString());
        Assert.True(message.Payload.Value.GetProperty("cleared").GetBoolean());
        Assert.Equal(0, message.Payload!.Value.GetProperty("resultCount").GetInt32());
        Assert.Equal("", message.Payload.Value.GetProperty("queryUsed").GetString());
        Assert.False(File.Exists(Path.Combine(fixture.Paths.MemoryChatsRoot, fixture.ChatId, "Abyss.Results.txt")));
        await fixture.Tasks.StopAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DisposalPreventsLatePublication()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Search(fixture.ChatId, "alpha", 4);
        var delayed = await fixture.Ui.TakeNextAsync();
        fixture.Runtime.Dispose();
        delayed();
        Assert.Empty(fixture.Sender.Messages);
        await fixture.Tasks.StopAsync(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("search")]
    [InlineData("retry_last")]
    [InlineData("last")]
    [InlineData("get_results")]
    [InlineData("clear_results")]
    public async Task CommandHandlersEchoTheClientRequestIdentity(string action)
    {
        using var fixture = new Fixture();
        fixture.Runtime.Search(fixture.ChatId, "alpha", 4, requestId: "seed");
        (await fixture.Ui.TakeNextAsync())();
        fixture.Sender.Messages.Clear();

        const string json = "{\"requestId\":\"client-request\",\"query\":\"beta\",\"count\":1}";
        using var document = JsonDocument.Parse(json);
        var command = new HostCommand("abyss.command." + action, json, fixture.ChatId,
            new Uri("https://chatgpt.com"), document.RootElement);
        var handlers = new AbyssCommandHandlers(fixture.Runtime, new NullTestLog());
        switch (action)
        {
            case "search": handlers.HandleSearch(command); break;
            case "retry_last": handlers.HandleRetryLast(command); break;
            case "last": handlers.HandleLast(command); break;
            case "get_results": handlers.HandleGetResults(command); break;
            case "clear_results": handlers.HandleClearResults(command); break;
        }
        if (action is "search" or "retry_last" or "last")
            (await fixture.Ui.TakeNextAsync())();

        var message = Assert.Single(fixture.Sender.Messages);
        Assert.Equal(fixture.ChatId, message.ChatId);
        Assert.Equal("client-request", message.Payload!.Value.GetProperty("requestId").GetString());
        Assert.Equal(action == "clear_results", message.Payload.Value.GetProperty("cleared").GetBoolean());
        await fixture.Tasks.StopAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StaleResultIdentityCannotFallBackToAnotherResultsIndex()
    {
        using var fixture = new Fixture();
        fixture.Runtime.Search(fixture.ChatId, "alpha", 4);
        (await fixture.Ui.TakeNextAsync())();
        var alphaId = Assert.Single(fixture.Sender.Messages).Payload!.Value
            .GetProperty("results")[0].GetProperty("id").GetString();
        fixture.Sender.Messages.Clear();
        fixture.Runtime.Search(fixture.ChatId, "beta", 4);
        (await fixture.Ui.TakeNextAsync())();
        fixture.Sender.Messages.Clear();

        fixture.Runtime.InjectResult(alphaId, 1, fixture.ChatId);
        Assert.Empty(fixture.Sender.Messages);
        fixture.Runtime.InjectResult(null, 1, fixture.ChatId);
        Assert.Equal("continuum.inject_text", Assert.Single(fixture.Sender.Messages).Name);
        await fixture.Tasks.StopAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RetryAfterRecentRecallRepeatsRecallInsteadOfSearchingTheDisplayLabel()
    {
        using var fixture = new Fixture();
        fixture.Runtime.FetchLast(fixture.ChatId, 1, inject: false, requestId: "recent");
        (await fixture.Ui.TakeNextAsync())();
        var original = Assert.Single(fixture.Sender.Messages).Payload!.Value;
        var id = original.GetProperty("results")[0].GetProperty("id").GetString();
        fixture.Sender.Messages.Clear();

        fixture.Runtime.RetryLast(fixture.ChatId, null, 4, "retry");
        (await fixture.Ui.TakeNextAsync())();
        var repeated = Assert.Single(fixture.Sender.Messages).Payload!.Value;
        Assert.Equal("retry", repeated.GetProperty("requestId").GetString());
        Assert.Equal(1, repeated.GetProperty("resultCount").GetInt32());
        Assert.Equal(id, repeated.GetProperty("results")[0].GetProperty("id").GetString());
        await fixture.Tasks.StopAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class Fixture : IDisposable
    {
        public TemporaryAppPaths Paths { get; } = new();
        public QueuedTestUiThread Ui { get; } = new();
        public RecordingRuntimeSender Sender { get; } = new();
        public BackgroundTaskSupervisor Tasks { get; } = new(new NullTestLog());
        public string ChatId { get; } = Guid.NewGuid().ToString();
        public TruthStore Truth { get; }
        public AbyssRuntime Runtime { get; }
        public Fixture()
        {
            Truth = new TruthStore(NullTruthTelemetryPublisher.Instance, Paths.MemoryChatsRoot);
            Truth.AppendTruthLine(ChatId, 'U', "alpha question");
            Truth.AppendTruthLine(ChatId, 'A', "alpha answer");
            Truth.AppendTruthLine(ChatId, 'U', "beta question");
            Truth.AppendTruthLine(ChatId, 'A', "beta answer");
            var session = new SessionContext();
            session.SetActiveChatId(ChatId);
            Runtime = new AbyssRuntime(new AbyssSearchService(new NullTestLog()), Sender, new NullToastHub(),
                session, Paths, Truth, new NullProcessLauncher(), Tasks, new NullTestLog(), Ui);
        }
        public void Dispose()
        {
            Runtime.Dispose();
            Tasks.Dispose();
            Truth.Dispose();
            Paths.Dispose();
        }
    }
}
