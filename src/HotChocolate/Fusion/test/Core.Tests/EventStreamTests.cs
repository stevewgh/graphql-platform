using CookieCrumble;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Fusion.Clients;
using HotChocolate.Fusion.Composition;
using HotChocolate.Fusion.Composition.Features;
using HotChocolate.Fusion.Planning;
using HotChocolate.Fusion.Shared;
using HotChocolate.Language;
using HotChocolate.Skimmed.Serialization;
using HotChocolate.Types.Relay;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static HotChocolate.Fusion.Shared.DemoProjectSchemaExtensions;
using static HotChocolate.Language.Utf8GraphQLParser;
using static HotChocolate.Fusion.TestHelper;

namespace HotChocolate.Fusion;

public class EventStreamTests(ITestOutputHelper output)
{
    private readonly Func<ICompositionLog> _logFactory = () => new TestCompositionLog(output);

    [Fact]
    public async Task Authors_And_Reviews_Subscription_OnNewReview()
    {
        // arrange
        using var cts = new CancellationTokenSource(10_000);
        using var demoProject = await DemoProject.CreateAsync(cts.Token);

        // act
        var fusionGraph = await new FusionGraphComposer(logFactory: _logFactory).ComposeAsync(
            new[]
            {
                demoProject.Reviews2.ToConfiguration(Reviews2ExtensionSdl, onlyHttp: true),
                demoProject.Accounts.ToConfiguration(AccountsExtensionSdl, onlyHttp: true)
            },
            default,
            cts.Token);

        var executor = await new ServiceCollection()
            .AddSingleton(demoProject.HttpClientFactory)
            .AddSingleton<IWebSocketConnectionFactory>(new NoWebSockets())
            .AddFusionGatewayServer()
            .ConfigureFromDocument(SchemaFormatter.FormatAsDocument(fusionGraph))
            .BuildRequestExecutorAsync(cancellationToken: cts.Token);

        var request = Parse(
            """
            subscription OnNewReview {
                onNewReview {
                    body
                    author {
                        name
                    }
                }
            }
            """);

        // act
        var result = await executor.ExecuteAsync(
            QueryRequestBuilder
                .New()
                .SetQuery(request)
                .Create(),
            cts.Token);

        // assert
        var snapshot = new Snapshot();
        await CollectStreamSnapshotData(snapshot, request, result, fusionGraph, cts.Token);
        await snapshot.MatchAsync(cts.Token);
    }

    private sealed class NoWebSockets : IWebSocketConnectionFactory
    {
        public IWebSocketConnection CreateConnection(string name)
        {
            throw new NotSupportedException();
        }
    }
}
