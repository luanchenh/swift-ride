using MatchingPricing_Service.Application.Services;
using MatchingPricing_Service.Domain;
using MatchingPricing_Service.Infrastructure.Persistence.Impl;
using MongoDB.Driver.GeoJsonObjectModel;
using Moq;

namespace SwiftRide.Tests.MatchingPricing;

public sealed class DriverLocationServiceTests
{
    private readonly Mock<IDriverLocationRepository> _repository = new();

    [Fact]
    public async Task FindByDriverUuid_Found_MapsAllFields()
    {
        var uuid = Guid.NewGuid();
        var updatedAt = DateTime.UtcNow;
        _repository.Setup(repository => repository.FindByDriverUuidAsync(uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLocation(uuid, 10.5, 106.7, true, false, updatedAt));
        var service = new DriverLocationService(_repository.Object);

        var result = await service.FindByDriverUuidAsync(uuid, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(uuid, result.DriverUuid);
        Assert.Equal(10.5, result.Latitude);
        Assert.Equal(106.7, result.Longitude);
        Assert.True(result.IsOnline);
        Assert.False(result.IsAvailable);
        Assert.Equal(updatedAt, result.UpdatedAt);
    }

    [Fact]
    public async Task FindByDriverUuid_NotFound_ReturnsNull()
    {
        _repository.Setup(repository => repository.FindByDriverUuidAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DriverLocation?)null);
        var service = new DriverLocationService(_repository.Object);

        var result = await service.FindByDriverUuidAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateLocation_ValidCoordinates_UpdatesAndMaps()
    {
        var uuid = Guid.NewGuid();
        _repository.Setup(repository => repository.UpdateLocationAsync(
                uuid, 10.75, 106.67, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLocation(uuid, 10.75, 106.67));
        var service = new DriverLocationService(_repository.Object);

        var result = await service.UpdateLocationAsync(uuid, 10.75, 106.67, CancellationToken.None);

        Assert.Equal(uuid, result.DriverUuid);
        Assert.Equal(10.75, result.Latitude);
        Assert.Equal(106.67, result.Longitude);
    }

    [Fact]
    public async Task UpdateAvailability_ForwardsRequest()
    {
        var uuid = Guid.NewGuid();
        var service = new DriverLocationService(_repository.Object);

        await service.UpdateAvailabilityAsync(uuid, true, CancellationToken.None);

        _repository.Verify(repository => repository.SetAvailabilityAsync(
            uuid, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateOnlineStatus_ForwardsRequest()
    {
        var uuid = Guid.NewGuid();
        var service = new DriverLocationService(_repository.Object);

        await service.UpdateOnlineStatus(uuid, false, CancellationToken.None);

        _repository.Verify(repository => repository.SetOnlineStatusAsync(
            uuid, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    [InlineData(0, double.PositiveInfinity)]
    public async Task UpdateLocation_InvalidCoordinates_Throws(double latitude, double longitude)
    {
        var service = new DriverLocationService(_repository.Object);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.UpdateLocationAsync(
            Guid.NewGuid(), latitude, longitude, CancellationToken.None));

        _repository.Verify(repository => repository.UpdateLocationAsync(
            It.IsAny<Guid>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Operations_EmptyDriverUuid_ThrowBeforeRepositoryCall()
    {
        var service = new DriverLocationService(_repository.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.FindByDriverUuidAsync(Guid.Empty, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAvailabilityAsync(Guid.Empty, true, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateOnlineStatus(Guid.Empty, true, CancellationToken.None));
    }

    private static DriverLocation CreateLocation(
        Guid uuid,
        double latitude,
        double longitude,
        bool online = true,
        bool available = true,
        DateTime? updatedAt = null) => new()
    {
        DriverUuid = uuid,
        Location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
            new GeoJson2DGeographicCoordinates(longitude, latitude)),
        IsOnline = online,
        IsAvailable = available,
        UpdatedAt = updatedAt ?? DateTime.UtcNow
    };
}
