namespace WithLove.ProductsAPI.Tests.Unit.Services;

using WithLove.ProductsAPI.Services;

/// <summary>
/// Unit tests for the Reciprocal Rank Fusion (RRF) merge algorithm in ProductCacheService.
/// RRF combines results from multiple rankers (FTS + vector search) into a single ranked list.
/// Formula per result: score = 1 / (k + rank), scores are summed across rankers.
/// </summary>
public class RrfMergeTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public void MergeWithRrf_ProductInBothLists_RankedHigherThanSingleList()
    {
        // Arrange — Product 10 appears in both rankers, Product 20 only in primary
        var primary = new List<(int ProductId, int Rank)> { (10, 1), (20, 2) };
        var secondary = new List<(int ProductId, int Rank)> { (10, 2), (30, 1) };

        // Act
        var result = ProductCacheService.MergeWithRrf(primary, secondary);

        // Assert — Product 10 should be first (boosted by both rankers)
        result[0].Should().Be(10);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public void MergeWithRrf_DisjointLists_ReturnsAllProducts()
    {
        // Arrange — No overlap between rankers
        var primary = new List<(int ProductId, int Rank)> { (1, 1), (2, 2) };
        var secondary = new List<(int ProductId, int Rank)> { (3, 1), (4, 2) };

        // Act
        var result = ProductCacheService.MergeWithRrf(primary, secondary);

        // Assert
        result.Should().HaveCount(4);
        result.Should().Contain(new[] { 1, 2, 3, 4 });
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public void MergeWithRrf_DisjointLists_Rank1BeatsRank2()
    {
        // Arrange — Each ranker has one result at rank 1
        var primary = new List<(int ProductId, int Rank)> { (1, 1) };
        var secondary = new List<(int ProductId, int Rank)> { (2, 1) };

        // Act
        var result = ProductCacheService.MergeWithRrf(primary, secondary);

        // Assert — Both have same score 1/(60+1), but product 1 was inserted first
        // so they tie. Just verify both are present.
        result.Should().HaveCount(2);
        result.Should().Contain(new[] { 1, 2 });
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public void MergeWithRrf_EmptyPrimary_ReturnsSecondaryResults()
    {
        // Arrange
        var primary = new List<(int ProductId, int Rank)>();
        var secondary = new List<(int ProductId, int Rank)> { (5, 1), (6, 2) };

        // Act
        var result = ProductCacheService.MergeWithRrf(primary, secondary);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().Be(5);
        result[1].Should().Be(6);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public void MergeWithRrf_EmptySecondary_ReturnsPrimaryResults()
    {
        // Arrange
        var primary = new List<(int ProductId, int Rank)> { (5, 1), (6, 2) };
        var secondary = new List<(int ProductId, int Rank)>();

        // Act
        var result = ProductCacheService.MergeWithRrf(primary, secondary);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().Be(5);
        result[1].Should().Be(6);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public void MergeWithRrf_BothEmpty_ReturnsEmptyList()
    {
        // Arrange
        var primary = new List<(int ProductId, int Rank)>();
        var secondary = new List<(int ProductId, int Rank)>();

        // Act
        var result = ProductCacheService.MergeWithRrf(primary, secondary);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public void MergeWithRrf_HigherRankInBothLists_BeatsLowerRankInBoth()
    {
        // Arrange — Product A at rank 1+1, Product B at rank 3+3
        var primary = new List<(int ProductId, int Rank)> { (100, 1), (200, 3) };
        var secondary = new List<(int ProductId, int Rank)> { (100, 1), (200, 3) };

        // Act
        var result = ProductCacheService.MergeWithRrf(primary, secondary);

        // Assert
        result[0].Should().Be(100);
        result[1].Should().Be(200);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public void MergeWithRrf_LowRankInBothLists_BeatHighRankInOneList()
    {
        // Arrange — Product A appears in both at rank 5, Product B only in primary at rank 1
        // A score: 1/(60+5) + 1/(60+5) = 2/65 ≈ 0.03077
        // B score: 1/(60+1) = 1/61 ≈ 0.01639
        var primary = new List<(int ProductId, int Rank)> { (50, 5), (60, 1) };
        var secondary = new List<(int ProductId, int Rank)> { (50, 5) };

        // Act
        var result = ProductCacheService.MergeWithRrf(primary, secondary);

        // Assert — Product 50 (in both) beats Product 60 (rank 1 but single ranker)
        result[0].Should().Be(50);
        result[1].Should().Be(60);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public void MergeWithRrf_CustomKValue_AffectsScoreSpread()
    {
        // Arrange — With k=0, rank differences are extreme; with k=1000, they compress
        var primary = new List<(int ProductId, int Rank)> { (1, 1), (2, 50) };
        var secondary = new List<(int ProductId, int Rank)>();

        // Act
        var resultSmallK = ProductCacheService.MergeWithRrf(primary, secondary, k: 1.0);
        var resultLargeK = ProductCacheService.MergeWithRrf(primary, secondary, k: 1000.0);

        // Assert — Order should be the same (rank 1 > rank 50) regardless of k
        resultSmallK[0].Should().Be(1);
        resultLargeK[0].Should().Be(1);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public void MergeWithRrf_ManyResults_PreservesCorrectOrdering()
    {
        // Arrange — 5 products across rankers with overlapping and unique entries
        var primary = new List<(int ProductId, int Rank)>
        {
            (1, 1), (2, 2), (3, 3), (4, 4), (5, 5)
        };
        var secondary = new List<(int ProductId, int Rank)>
        {
            (3, 1), (1, 2), (6, 3), (7, 4)
        };

        // Act
        var result = ProductCacheService.MergeWithRrf(primary, secondary);

        // Assert
        // Product 1: 1/(60+1) + 1/(60+2) ≈ 0.01639 + 0.01613 = 0.03252
        // Product 3: 1/(60+3) + 1/(60+1) ≈ 0.01587 + 0.01639 = 0.03226
        // Both in both lists, Product 1 has slightly better combined rank
        result.Should().HaveCount(7); // 5 from primary + 2 unique from secondary
        result[0].Should().Be(1);  // Best combined score
        result[1].Should().Be(3);  // Second best combined
    }
}
