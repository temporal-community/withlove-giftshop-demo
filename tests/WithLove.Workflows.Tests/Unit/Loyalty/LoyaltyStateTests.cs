namespace WithLove.Workflows.Tests.Unit.Loyalty;

/// <summary>
/// Pure unit tests for <see cref="LoyaltyState"/> record logic.
/// No Temporal test environment needed — all assertions operate directly on the record.
/// </summary>
public class LoyaltyStateTests
{
    // ─── Tier threshold boundaries ─────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public void Tier_IsBronze_WhenLifetimeEarned_Is0()
    {
        var state = LoyaltyState.Empty;

        state.Tier.Should().Be(LoyaltyTier.Bronze);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public void Tier_IsBronze_WhenLifetimeEarned_Is499()
    {
        var state = LoyaltyState.Empty with { LifetimeEarned = 499 };

        state.Tier.Should().Be(LoyaltyTier.Bronze);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public void Tier_IsSilver_WhenLifetimeEarned_Is500()
    {
        var state = LoyaltyState.Empty with { LifetimeEarned = 500 };

        state.Tier.Should().Be(LoyaltyTier.Silver);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public void Tier_IsSilver_WhenLifetimeEarned_Is1999()
    {
        var state = LoyaltyState.Empty with { LifetimeEarned = 1999 };

        state.Tier.Should().Be(LoyaltyTier.Silver);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public void Tier_IsGold_WhenLifetimeEarned_Is2000()
    {
        var state = LoyaltyState.Empty with { LifetimeEarned = 2000 };

        state.Tier.Should().Be(LoyaltyTier.Gold);
    }

    // ─── Tier driven by LifetimeEarned, not Balance ────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public void Tier_StaysSilver_WhenBalance_IsZero_ButLifetimeEarned_Is500()
    {
        // Customer earned 500 lifetime, then redeemed all — tier must not regress
        var state = LoyaltyState.Empty with
        {
            Balance = 0,
            LifetimeEarned = 500
        };

        state.Tier.Should().Be(LoyaltyTier.Silver);
    }

    // ─── TrimAndExpire behavior ────────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public void TrimAndExpire_RestoresBalance_ForExpiredPendingRedemption()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var redemptionId = "red-001";
        var pending = new PendingRedemption(redemptionId, 100, 1.00m, DateTime.UtcNow.AddHours(-25));

        var state = LoyaltyState.Empty with
        {
            Balance = 400,
            PendingRedemptions = new Dictionary<string, PendingRedemption>
            {
                [redemptionId] = pending
            }
        };

        var result = state.TrimAndExpire(cutoff);

        result.Balance.Should().Be(500); // 400 + 100 restored
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public void TrimAndExpire_MovesExpiredRedemptionId_ToCancelledIds()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var redemptionId = "red-002";
        var pending = new PendingRedemption(redemptionId, 100, 1.00m, DateTime.UtcNow.AddHours(-25));

        var state = LoyaltyState.Empty with
        {
            Balance = 400,
            PendingRedemptions = new Dictionary<string, PendingRedemption>
            {
                [redemptionId] = pending
            }
        };

        var result = state.TrimAndExpire(cutoff);

        result.CancelledRedemptionIds.Should().Contain(redemptionId);
        result.PendingRedemptions.Should().NotContainKey(redemptionId);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public void TrimAndExpire_Preserves_ActivePendingRedemption()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var redemptionId = "red-003";
        var pending = new PendingRedemption(redemptionId, 100, 1.00m, DateTime.UtcNow.AddHours(-1));

        var state = LoyaltyState.Empty with
        {
            Balance = 400,
            PendingRedemptions = new Dictionary<string, PendingRedemption>
            {
                [redemptionId] = pending
            }
        };

        var result = state.TrimAndExpire(cutoff);

        result.Balance.Should().Be(400); // unchanged — reservation still active
        result.PendingRedemptions.Should().ContainKey(redemptionId);
        result.CancelledRedemptionIds.Should().NotContain(redemptionId);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Loyalty)]
    public void TrimAndExpire_Trims_TransactionHistory_ToLast500()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var transactions = Enumerable.Range(1, 600)
            .Select(i => new PointTransaction(
                $"cs_session_{i}",
                $"#WL-{i:D6}",
                10,
                "Purchase",
                DateTime.UtcNow.AddMinutes(-i)))
            .ToList();

        var state = LoyaltyState.Empty with { Transactions = transactions };

        var result = state.TrimAndExpire(cutoff);

        result.Transactions.Should().HaveCount(500);
    }
}
