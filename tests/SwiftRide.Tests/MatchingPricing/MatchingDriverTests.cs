using MatchingPricing_Service.Application.Contracts;
using MatchingPricing_Service.Application.Services;
using MatchingPricing_Service.Domain;
using MatchingPricing_Service.Infrastructure.Persistence.Impl;
using Microsoft.Extensions.Options;
using MongoDB.Driver.GeoJsonObjectModel;
using Moq;

namespace SwiftRide.Tests.MatchingPricing;

public sealed class MatchingDriverTests
{
    private readonly Mock<IDriverLocationRepository> _repository = new();

    [Fact]
    public async Task Match_DriverFound_ReturnsSuccessAndForwardsFilters()
    {
        var tripUuid = Guid.NewGuid();
        var driverUuid = Guid.NewGuid();
        var excluded = new[] { Guid.NewGuid() };
        var pickup = new LocationContract(10.7, 106.6);
        _repository.Setup(repository => repository.FindNearestAvailableAsync(
                pickup, excluded, 7_500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriver(driverUuid));
        var service = CreateService();

        var result = await service.MatchAsync(new MatchingRequestContract
        {
            TripUuid = tripUuid,
            Pickup = pickup,
            ExcludeDriverUuids = excluded
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(tripUuid, result.TripUuid);
        Assert.Equal(driverUuid, result.DriverUuid);
    }

    [Fact]
    public async Task Match_NoDriver_ReturnsFailureWithEmptyUuid()
    {
        _repository.Setup(repository => repository.FindNearestAvailableAsync(
                It.IsAny<LocationContract>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DriverLocation?)null);
        var service = CreateService();

        var result = await service.MatchAsync(new MatchingRequestContract
        {
            TripUuid = Guid.NewGuid(),
            Pickup = new LocationContract(10, 106)
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Guid.Empty, result.DriverUuid);
    }

    [Fact]
    public async Task Match_EmptyTripUuid_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.MatchAsync(
            new MatchingRequestContract
            {
                TripUuid = Guid.Empty,
                Pickup = new LocationContract(10, 106)
            }, CancellationToken.None));
    }

    [Theory]
    [InlineData(-91, 106)]
    [InlineData(91, 106)]
    [InlineData(double.NaN, 106)]
    [InlineData(10, -181)]
    [InlineData(10, 181)]
    [InlineData(10, double.PositiveInfinity)]
    public async Task Match_InvalidCoordinates_Throws(double latitude, double longitude)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.MatchAsync(
            new MatchingRequestContract
            {
                TripUuid = Guid.NewGuid(),
                Pickup = new LocationContract(latitude, longitude)
            }, CancellationToken.None));
    }

    private MatchingDriver CreateService() => new(
        _repository.Object,
        Microsoft.Extensions.Options.Options.Create(new MatchingSetting { MaxDistanceMeters = 7_500 }));

    private static DriverLocation CreateDriver(Guid uuid) => new()
    {
        DriverUuid = uuid,
        Location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
            new GeoJson2DGeographicCoordinates(106.6, 10.7))
    };
}
