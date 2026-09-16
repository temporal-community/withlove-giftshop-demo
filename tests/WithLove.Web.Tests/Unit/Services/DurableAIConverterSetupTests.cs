using Microsoft.Extensions.AI;
using TemporalCommunity.Extensions.AI;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;
using WithLove.Workflows.Loyalty;

namespace WithLove.Web.Tests.Unit.Services;

/// <summary>
/// Pins how <c>DurableAIConverterSetup.Resolve</c> reconciles the Temporal client's data converter
/// with the MEAI-aware one the chat workflow needs.
/// </summary>
/// <remarks>
/// <para>
/// Every branch here fails silently in production if it regresses. The package's own plugin applies
/// <c>DurableAIDataConverter</c> only while the converter is still <c>DataConverter.Default</c> and
/// otherwise logs and skips — and the blast radius of that skip is asymmetric: arguments sent to
/// the worker survive, but results read back bind to default values with no exception. A
/// <c>LoyaltyProfile</c> that quietly reads <c>0</c> is indistinguishable from a customer with no
/// points. So the reconciliation is asserted here rather than trusted.
/// </para>
/// <para>
/// The assertions are deliberately behavioural. The AI converter and the stock converter are the
/// same CLR type — <c>DurableAIDataConverter</c> reuses Temporal's <c>DefaultPayloadConverter</c>
/// and only swaps its <c>JsonSerializerOptions</c> — so any test written against <c>GetType()</c>
/// would report a stock, unconverted client as "already AI-aware" and pass while the bug shipped.
/// <see cref="BothConverters_ShareOneClrType_WhichIsWhyResolveComparesByReference"/> proves that
/// trap is real rather than hypothetical, and
/// <see cref="AiWrittenPayload_ReadByStockConverter_SilentlyZeroesEveryField"/> reproduces the
/// damage it does.
/// </para>
/// </remarks>
public class DurableAIConverterSetupTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void Resolve_StockConverter_AdoptsTheAiAwareConverter()
    {
        var resolved = DurableAIConverterSetup.Resolve(DataConverter.Default);

        resolved.PayloadConverter.Should().BeSameAs(
            DurableAIDataConverter.Instance.PayloadConverter);
        RoundTrip(resolved, SampleToolCall())
            .Contents.Should().ContainSingle()
            .Which.Should().BeOfType<FunctionCallContent>()
            .Which.Name.Should().Be("search_products");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void Resolve_StockConverterWithPayloadCodec_AdoptsConverterAndKeepsTheCodec()
    {
        // The case the original prescription got wrong. Assigning DurableAIDataConverter.Instance
        // unconditionally would have dropped the codec — disabling encryption or compression just
        // as silently as the plugin drops the AI converter, and in a direction nobody would notice
        // until payloads were already on the wire in the clear.
        var codec = new MarkerPayloadCodec();
        var configured = DataConverter.Default with { PayloadCodec = codec };

        var resolved = DurableAIConverterSetup.Resolve(configured);

        resolved.PayloadCodec.Should().BeSameAs(codec, "a codec must survive converter adoption");
        resolved.PayloadConverter.Should().BeSameAs(
            DurableAIDataConverter.Instance.PayloadConverter);
        RoundTrip(resolved, SampleToolCall())
            .Contents.Should().ContainSingle()
            .Which.Should().BeOfType<FunctionCallContent>();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void Resolve_AlreadyAiAwareConverter_IsLeftUntouchedIncludingItsCodec()
    {
        var codec = new MarkerPayloadCodec();
        var configured = DurableAIDataConverter.Instance with { PayloadCodec = codec };

        var resolved = DurableAIConverterSetup.Resolve(configured);

        resolved.Should().BeSameAs(configured, "an AI-aware converter needs no reconciliation");
        resolved.PayloadCodec.Should().BeSameAs(codec);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void Resolve_CustomPayloadConverter_ThrowsAtStartup()
    {
        var configured = DataConverter.Default with
        {
            PayloadConverter = A.Fake<IPayloadConverter>(),
        };

        var resolve = () => DurableAIConverterSetup.Resolve(configured);

        // Failing at startup is the whole point: the alternative is a client that starts happily
        // and returns zeroed results forever.
        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be reconciled with DurableAIDataConverter*");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void Resolve_CustomFailureConverter_ThrowsAtStartup()
    {
        var configured = DataConverter.Default with
        {
            FailureConverter = A.Fake<IFailureConverter>(),
        };

        var resolve = () => DurableAIConverterSetup.Resolve(configured);

        resolve.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void Resolve_NullConverter_Throws()
    {
        var resolve = () => DurableAIConverterSetup.Resolve(null!);

        resolve.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void AiWrittenPayload_ReadByStockConverter_SilentlyZeroesEveryField()
    {
        // This is the damage a skipped reconciliation actually does, reproduced end to end.
        //
        // The two converters differ in wire format, not in type handling: DurableAI writes
        // camelCase, the stock converter writes PascalCase and reads case-sensitively. So a result
        // written by the AI-aware worker and read by a stock client binds nothing — and System.Text
        // .Json reports no error for properties it simply did not find.
        var profile = new LoyaltyProfile(650, 650, LoyaltyTier.Silver, 1350);
        var writtenByWorker = DurableAIDataConverter.Instance.PayloadConverter.ToPayload(profile);

        var readByStockClient = DataConverter.Default.PayloadConverter
            .ToValue<LoyaltyProfile>(writtenByWorker);

        readByStockClient.Should().NotBeNull("no exception is thrown — that is the problem");
        readByStockClient.Balance.Should().Be(0);
        readByStockClient.LifetimeEarned.Should().Be(0);
        readByStockClient.PointsToNextTier.Should().Be(0);
        readByStockClient.Tier.Should().Be(
            LoyaltyTier.Bronze,
            "zero is a plausible tier, so the customer is shown a wrong answer rather than an error");

        // And the reconciled converter — the one Resolve hands back — reads it correctly.
        var reconciled = DurableAIConverterSetup.Resolve(DataConverter.Default);
        reconciled.PayloadConverter.ToValue<LoyaltyProfile>(writtenByWorker)
            .Should().Be(profile);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void BothConverters_ShareOneClrType_WhichIsWhyResolveComparesByReference()
    {
        // Guards Resolve's reasoning rather than Resolve itself. A type comparison here would
        // report every stock client as "already AI-aware" and reintroduce the silent skip. If a
        // future SDK release splits the types, this test goes red and the reference comparison can
        // be revisited deliberately instead of by assumption.
        DataConverter.Default.PayloadConverter.GetType().Should().Be(
            DurableAIDataConverter.Instance.PayloadConverter.GetType());
        DataConverter.Default.PayloadConverter.Should().NotBeSameAs(
            DurableAIDataConverter.Instance.PayloadConverter);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public void StockWrittenPayload_ReadByAiConverter_StillBinds()
    {
        // The asymmetry that makes the failure so hard to spot: the direction that works is the
        // one exercised on every request (arguments travelling to the worker), and the direction
        // that silently fails is the one carrying results back.
        var profile = new LoyaltyProfile(650, 650, LoyaltyTier.Silver, 1350);
        var writtenByStockClient = DataConverter.Default.PayloadConverter.ToPayload(profile);

        DurableAIDataConverter.Instance.PayloadConverter
            .ToValue<LoyaltyProfile>(writtenByStockClient)
            .Should().Be(profile);
    }

    private static ChatMessage SampleToolCall() =>
        new(ChatRole.Assistant,
        [
            new FunctionCallContent(
                "call-1",
                "search_products",
                new Dictionary<string, object?> { ["query"] = "keepsake" }),
        ]);

    private static ChatMessage RoundTrip(DataConverter converter, ChatMessage message)
    {
        var payload = converter.PayloadConverter.ToPayload(message);
        return converter.PayloadConverter.ToValue<ChatMessage>(payload);
    }

    /// <summary>A codec that is trivially identifiable, so "was it kept?" is unambiguous.</summary>
    private sealed class MarkerPayloadCodec : IPayloadCodec
    {
        public Task<IReadOnlyCollection<Payload>> EncodeAsync(IReadOnlyCollection<Payload> payloads) =>
            Task.FromResult(payloads);

        public Task<IReadOnlyCollection<Payload>> DecodeAsync(IReadOnlyCollection<Payload> payloads) =>
            Task.FromResult(payloads);
    }
}
