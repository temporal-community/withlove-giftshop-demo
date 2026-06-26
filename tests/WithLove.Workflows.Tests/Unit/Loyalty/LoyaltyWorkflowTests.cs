namespace WithLove.Workflows.Tests.Unit.Loyalty;

/// <summary>
/// Temporal workflow tests for <see cref="LoyaltyAccountWorkflow"/>.
/// Uses the time-skipping environment to test expiry without real-time waits.
/// </summary>
public class LoyaltyWorkflowTests
{
    // ─── Helper ───────────────────────────────────────────────────────────────

    private static TemporalWorkerOptions WorkerOptions() =>
        new TemporalWorkerOptions($"loyalty-test-{Guid.NewGuid()}")
            .AddWorkflow<LoyaltyAccountWorkflow>();

    // ─── Earn points ──────────────────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public async Task EarnPoints_IncreasesBalance_AndLifetimeEarned()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(env.Client, WorkerOptions());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(id: $"loyalty-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            await handle.SignalAsync(wf => wf.EarnPointsAsync(
                new EarnPointsInput("cs_test_earn_001", "#WL-000001", 100)));

            var balance = await handle.QueryAsync(wf => wf.GetBalance());
            var profile = await handle.QueryAsync(wf => wf.GetLoyaltyProfile());

            balance.Should().Be(100);
            profile.LifetimeEarned.Should().Be(100);
        });
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public async Task EarnPoints_IsIdempotent_ForDuplicateStripeSessionId()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(env.Client, WorkerOptions());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(id: $"loyalty-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            var input = new EarnPointsInput("cs_test_idempotent_001", "#WL-000002", 100);

            await handle.SignalAsync(wf => wf.EarnPointsAsync(input));
            await handle.SignalAsync(wf => wf.EarnPointsAsync(input)); // duplicate

            var balance = await handle.QueryAsync(wf => wf.GetBalance());

            balance.Should().Be(100); // not 200
        });
    }

    // ─── Reserve points (Update) ──────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public async Task ReservePoints_DeductsBalance_AndCreatesPendingRedemption()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(env.Client, WorkerOptions());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(id: $"loyalty-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            await handle.SignalAsync(wf => wf.EarnPointsAsync(
                new EarnPointsInput("cs_test_reserve_001", "#WL-000003", 500)));

            var result = await handle.ExecuteUpdateAsync(wf => wf.ReservePointsAsync(
                new ReservePointsInput(100)));

            var balance = await handle.QueryAsync(wf => wf.GetBalance());

            balance.Should().Be(400);
            result.RedemptionId.Should().NotBeNullOrEmpty();
            result.PointsReserved.Should().Be(100);
            result.DiscountAmount.Should().Be(1.00m);
        });
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public async Task ReservePoints_Fails_WhenInsufficientBalance()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(env.Client, WorkerOptions());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(id: $"loyalty-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            // No points earned — balance is zero
            Func<Task> act = () => handle.ExecuteUpdateAsync(wf => wf.ReservePointsAsync(
                new ReservePointsInput(100)));

            await act.Should().ThrowAsync<Exception>();
        });
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public async Task ReservePoints_Fails_WhenNotMultipleOf100()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(env.Client, WorkerOptions());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(id: $"loyalty-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            await handle.SignalAsync(wf => wf.EarnPointsAsync(
                new EarnPointsInput("cs_test_mod_001", "#WL-000004", 500)));

            Func<Task> act = () => handle.ExecuteUpdateAsync(wf => wf.ReservePointsAsync(
                new ReservePointsInput(150)));

            await act.Should().ThrowAsync<Exception>();
        });
    }

    // ─── Commit and cancel ────────────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public async Task CommitRedemption_WritesNegativeTransaction_AndClearsPending()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(env.Client, WorkerOptions());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(id: $"loyalty-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            await handle.SignalAsync(wf => wf.EarnPointsAsync(
                new EarnPointsInput("cs_test_commit_001", "#WL-000005", 500)));

            var result = await handle.ExecuteUpdateAsync(wf => wf.ReservePointsAsync(
                new ReservePointsInput(100)));

            await handle.SignalAsync(wf => wf.CommitRedemptionAsync(result.RedemptionId));

            var history = await handle.QueryAsync(wf => wf.GetTransactionHistory());

            history.Should().Contain(t => t.Points == -100 && t.Reason == "Redemption");
        });
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public async Task CancelRedemption_RestoresBalance_AndClearsPending()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(env.Client, WorkerOptions());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(id: $"loyalty-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            await handle.SignalAsync(wf => wf.EarnPointsAsync(
                new EarnPointsInput("cs_test_cancel_001", "#WL-000006", 500)));

            var result = await handle.ExecuteUpdateAsync(wf => wf.ReservePointsAsync(
                new ReservePointsInput(100)));

            await handle.SignalAsync(wf => wf.CancelRedemptionAsync(result.RedemptionId));

            var balance = await handle.QueryAsync(wf => wf.GetBalance());

            balance.Should().Be(500);
        });
    }

    // ─── 24-hour expiry (time-skipping) ──────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public async Task ExpiredReservation_AutoRestoresBalance_After24Hours()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(env.Client, WorkerOptions());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(id: $"loyalty-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            await handle.SignalAsync(wf => wf.EarnPointsAsync(
                new EarnPointsInput("cs_test_expiry_001", "#WL-000007", 500)));

            await handle.ExecuteUpdateAsync(wf => wf.ReservePointsAsync(
                new ReservePointsInput(100)));

            // Skip 25 hours — reservation should auto-expire and balance restored
            await env.DelayAsync(TimeSpan.FromHours(25));

            var balance = await handle.QueryAsync(wf => wf.GetBalance());

            balance.Should().Be(500);
        });
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public async Task CommitAfterExpiry_IsNoOp_AndDoesNotCorruptBalance()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(env.Client, WorkerOptions());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(id: $"loyalty-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            await handle.SignalAsync(wf => wf.EarnPointsAsync(
                new EarnPointsInput("cs_test_late_commit_001", "#WL-000008", 500)));

            var result = await handle.ExecuteUpdateAsync(wf => wf.ReservePointsAsync(
                new ReservePointsInput(100)));

            // Advance past the 24h expiry window — reservation is auto-cancelled
            await env.DelayAsync(TimeSpan.FromHours(25));

            // Late commit signal arrives after expiry — must be a safe no-op
            await handle.SignalAsync(wf => wf.CommitRedemptionAsync(result.RedemptionId));

            var balance = await handle.QueryAsync(wf => wf.GetBalance());

            balance.Should().Be(500); // balance fully restored, not double-counted
        });
    }

    // ─── Profile query ────────────────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public async Task GetLoyaltyProfile_ShowsCorrectTierAndProgress_ForBronzeCustomer()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var worker = new TemporalWorker(env.Client, WorkerOptions());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(id: $"loyalty-{Guid.NewGuid()}", taskQueue: worker.Options.TaskQueue!));

            await handle.SignalAsync(wf => wf.EarnPointsAsync(
                new EarnPointsInput("cs_test_profile_001", "#WL-000009", 300)));

            var profile = await handle.QueryAsync(wf => wf.GetLoyaltyProfile());

            profile.Tier.Should().Be(LoyaltyTier.Bronze);
            profile.PointsToNextTier.Should().Be(200); // 500 - 300
        });
    }
}
