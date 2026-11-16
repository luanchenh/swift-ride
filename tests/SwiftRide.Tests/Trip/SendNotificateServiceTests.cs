using Trip_Service.Application.Services;
using Trip_Service.Domain;

namespace SwiftRide.Tests.Trip;

public sealed class SendNotificateServiceTests
{
    [Fact]
    public void Notify_WithoutSubscriber_ReturnsFalseAndDoesNotCreateOffer()
    {
        var service = new SendNotificateService();
        var trip = new Trip_Service.Domain.Trip { Uuid = Guid.NewGuid() };

        var notified = service.Notify(Guid.NewGuid(), trip);

        Assert.False(notified);
        Assert.False(service.HasPendingOffer(trip.Uuid));
    }

    [Fact]
    public async Task SubscribeAndNotify_DeliversTripAndTracksPendingOffer()
    {
        var service = new SendNotificateService();
        var driverUuid = Guid.NewGuid();
        var trip = new Trip_Service.Domain.Trip { Uuid = Guid.NewGuid() };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = service.SubscribeAsync(driverUuid, cancellation.Token).GetAsyncEnumerator();
        var pendingRead = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => service.Notify(driverUuid, trip), cancellation.Token);

        Assert.True(await pendingRead);
        Assert.Same(trip, enumerator.Current);
        Assert.True(service.HasPendingOffer(trip.Uuid));
        Assert.True(service.IsPendingOffer(driverUuid, trip.Uuid));
        Assert.False(service.IsPendingOffer(Guid.NewGuid(), trip.Uuid));
    }

    [Fact]
    public async Task RemovePendingOffer_RemovesTrackedOffer()
    {
        var service = new SendNotificateService();
        var driverUuid = Guid.NewGuid();
        var trip = new Trip_Service.Domain.Trip { Uuid = Guid.NewGuid() };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = service.SubscribeAsync(driverUuid, cancellation.Token).GetAsyncEnumerator();
        var pendingRead = enumerator.MoveNextAsync().AsTask();
        await WaitUntilAsync(() => service.Notify(driverUuid, trip), cancellation.Token);
        await pendingRead;

        service.RemovePendingOffer(trip.Uuid);

        Assert.False(service.HasPendingOffer(trip.Uuid));
        Assert.False(service.IsPendingOffer(driverUuid, trip.Uuid));
    }

    [Fact]
    public async Task SecondSubscription_ReplacesAndCompletesPreviousSubscription()
    {
        var service = new SendNotificateService();
        var driverUuid = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var first = service.SubscribeAsync(driverUuid, cancellation.Token).GetAsyncEnumerator();
        var firstRead = first.MoveNextAsync().AsTask();
        await Task.Yield();
        await using var second = service.SubscribeAsync(driverUuid, cancellation.Token).GetAsyncEnumerator();
        var secondRead = second.MoveNextAsync().AsTask();
        var trip = new Trip_Service.Domain.Trip { Uuid = Guid.NewGuid() };
        await WaitUntilAsync(() => service.Notify(driverUuid, trip), cancellation.Token);

        Assert.False(await firstRead);
        Assert.True(await secondRead);
        Assert.Same(trip, second.Current);
    }

    private static async Task WaitUntilAsync(Func<bool> action, CancellationToken cancellationToken)
    {
        while (!action())
        {
            await Task.Delay(10, cancellationToken);
        }
    }
}
