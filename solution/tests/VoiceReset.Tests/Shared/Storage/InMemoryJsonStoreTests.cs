using VoiceReset.Shared.Storage;

namespace VoiceReset.Tests.Shared.Storage;

public sealed class InMemoryJsonStoreTests
{
    private sealed record Sample(string Name, int Count);

    [Fact]
    public async Task WriteThenRead_SameKey_ReturnsEqualValue()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryJsonStore();

        // Act
        await store.WriteAsync("mock/state.json", new Sample("a", 1), ct);
        var read = await store.ReadAsync<Sample>("mock/state.json", ct);
        var missing = await store.ReadAsync<Sample>("mock/other.json", ct);

        // Assert
        Assert.Equal(new Sample("a", 1), read);
        Assert.Null(missing);
    }

    [Fact]
    public async Task ListKeys_WithPrefix_ReturnsOnlyMatchingKeysInOrder()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryJsonStore();
        await store.WriteAsync("sessions/b.json", new Sample("b", 2), ct);
        await store.WriteAsync("mock/state.json", new Sample("m", 0), ct);
        await store.WriteAsync("sessions/a.json", new Sample("a", 1), ct);

        // Act
        var keys = await store.ListKeysAsync("sessions/", ct);

        // Assert
        Assert.Collection(
            keys,
            key => Assert.Equal("sessions/a.json", key),
            key => Assert.Equal("sessions/b.json", key));
    }
}
