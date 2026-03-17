namespace WithLove.ProductsAPI.Tests.Fixtures;

/// <summary>
/// Defines a shared collection so all integration test classes use a single
/// AppHost instance (one set of Docker containers) instead of one per class.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}
