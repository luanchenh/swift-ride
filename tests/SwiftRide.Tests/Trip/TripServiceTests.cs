using Moq;
using Trip_Service.Application.Contracts.Trip;
using Trip_Service.Application.Services;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Persistence.Impl;

namespace SwiftRide.Tests.Trip;

public sealed class TripServiceTests
{
    private readonly Mock<IAccountRepository> _accounts = new();
    private readonly Mock<ITripRepository> _trips = new();

    [Fact]
    public async Task GetTripByUuid_Found_ReturnsTrip()
    {
        var trip = new Trip_Service.Domain.Trip { Uuid = Guid.NewGuid() };
        _trips.Setup(repository => repository.FindByUuidAsync(trip.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trip);
        var service = CreateService();

        var result = await service.GetTripByUuidAsync(trip.Uuid);

        Assert.Same(trip, result);
    }

    [Fact]
    public async Task GetTripByUuid_NotFound_Throws()
    {
        _trips.Setup(repository => repository.FindByUuidAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Trip_Service.Domain.Trip?)null);
        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetTripByUuidAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateTrip_ValidRequest_TrimsNamesAndPersistsRequestedTrip()
    {
        var riderUuid = Guid.NewGuid();
        _trips.Setup(repository => repository.FindActiveByRiderAsync(
                riderUuid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Trip_Service.Domain.Trip?)null);
        _accounts.Setup(repository => repository.FindByUuidAsync(riderUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account { Uuid = riderUuid, Role = Role.Rider });
        var before = DateTime.UtcNow;
        var service = CreateService();

        var result = await service.CreateTripAsync(riderUuid, ValidRequest());

        Assert.NotEqual(Guid.Empty, result.Uuid);
        Assert.Equal(riderUuid, result.RiderUuid);
        Assert.Equal("Bến Thành", result.PickupName);
        Assert.Equal("Sân bay", result.DestinationName);
        Assert.Equal(TripStatus.Requested, result.Status);
        Assert.InRange(result.CreatedAt, before, DateTime.UtcNow);
        _trips.Verify(repository => repository.AddAsync(result, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTrip_ActiveTripExists_ThrowsBeforeAccountLookup()
    {
        _trips.Setup(repository => repository.FindActiveByRiderAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Trip_Service.Domain.Trip());
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateTripAsync(Guid.NewGuid(), ValidRequest()));

        _accounts.Verify(repository => repository.FindByUuidAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateTrip_AccountMissing_Throws()
    {
        _trips.Setup(repository => repository.FindActiveByRiderAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Trip_Service.Domain.Trip?)null);
        _accounts.Setup(repository => repository.FindByUuidAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);
        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CreateTripAsync(Guid.NewGuid(), ValidRequest()));
    }

    [Fact]
    public async Task CreateTrip_DriverAccount_Throws()
    {
        _trips.Setup(repository => repository.FindActiveByRiderAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Trip_Service.Domain.Trip?)null);
        _accounts.Setup(repository => repository.FindByUuidAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account { Role = Role.Driver });
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateTripAsync(Guid.NewGuid(), ValidRequest()));
    }

    [Fact]
    public async Task CreateTrip_IdenticalCoordinates_Throws()
    {
        _trips.Setup(repository => repository.FindActiveByRiderAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Trip_Service.Domain.Trip?)null);
        _accounts.Setup(repository => repository.FindByUuidAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account { Role = Role.Rider });
        var request = ValidRequest();
        request.Destination.Latitude = request.Pickup.Latitude;
        request.Destination.Longitude = request.Pickup.Longitude;
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateTripAsync(Guid.NewGuid(), request));

        _trips.Verify(repository => repository.AddAsync(
            It.IsAny<Trip_Service.Domain.Trip>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateTrip_RefreshesUpdatedAtAndPersists()
    {
        var oldTime = DateTime.UtcNow.AddDays(-1);
        var trip = new Trip_Service.Domain.Trip { UpdatedAt = oldTime };
        var service = CreateService();

        await service.UpdateTripAsync(trip);

        Assert.True(trip.UpdatedAt > oldTime);
        _trips.Verify(repository => repository.UpdateAsync(trip, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryMethods_ForwardArgumentsAndResults()
    {
        var riderUuid = Guid.NewGuid();
        var driverUuid = Guid.NewGuid();
        var excluded = Guid.NewGuid();
        var riderActive = new Trip_Service.Domain.Trip();
        var driverActive = new Trip_Service.Domain.Trip();
        IReadOnlyList<Trip_Service.Domain.Trip> history = [new()];
        _trips.Setup(repository => repository.FindActiveByRiderAsync(riderUuid, excluded, It.IsAny<CancellationToken>()))
            .ReturnsAsync(riderActive);
        _trips.Setup(repository => repository.FindActiveByDriverAsync(driverUuid, excluded, It.IsAny<CancellationToken>()))
            .ReturnsAsync(driverActive);
        _trips.Setup(repository => repository.FindHistoryByRiderAsync(riderUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _trips.Setup(repository => repository.FindHistoryByDriverAsync(driverUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        var service = CreateService();

        Assert.Same(riderActive, await service.GetActiveTripByRiderAsync(riderUuid, excluded));
        Assert.Same(driverActive, await service.GetActiveTripByDriverAsync(driverUuid, excluded));
        Assert.Same(history, await service.GetTripHistoryByRiderAsync(riderUuid));
        Assert.Same(history, await service.GetTripHistoryByDriverAsync(driverUuid));
    }

    private TripService CreateService() => new(_accounts.Object, _trips.Object);

    private static CreateTripRequestContract ValidRequest() => new()
    {
        Pickup = new GeoLocationContract { Name = "  Bến Thành  ", Latitude = 10.772, Longitude = 106.698 },
        Destination = new GeoLocationContract { Name = "  Sân bay  ", Latitude = 10.819, Longitude = 106.652 }
    };
}
