using MatchingPricing_Service.Application.Contracts;
using MatchingPricing_Service.Application.Services;
using MatchingPricing_Service.Application.Services.Impl;
using MatchingPricing_Service.Application.Services.Util;
using MatchingPricing_Service.Domain;
using Moq;

namespace SwiftRide.Tests.MatchingPricing;

public sealed class QuoteServiceTests
{
    private readonly Mock<IOSRMService> _osrm = new();
    private readonly Mock<IPricingService> _pricing = new();

    [Fact]
    public async Task RequestQuote_MapsRouteAndCalculatedPrice()
    {
        var route = new OSRMRouteContract { Distance = 12_500, Duration = 1_800 };
        _osrm.Setup(service => service.GetRouteAsync(
                It.Is<IReadOnlyList<LocationContract>>(locations =>
                    locations.Count == 2 &&
                    locations[0].Latitude == 10.1 &&
                    locations[1].Longitude == 106.9),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OSRMResponseContract { Routes = [route] });
        _pricing.Setup(service => service.ProcessPrice(12.5, 30, WeatherCondition.Rain, 1))
            .Returns(150_000m);
        _pricing.Setup(service => service.GetMultiplier(WeatherCondition.Rain)).Returns(1.5m);
        var service = new QuoteService(_osrm.Object, _pricing.Object);

        var result = await service.RequestQuoteAsync(new QuoteRequestContract
        {
            Pickup = new LocationContract(10.1, 106.1),
            Destination = new LocationContract(10.9, 106.9),
            WeatherCondition = WeatherCondition.Rain
        }, CancellationToken.None);

        Assert.Equal(150_000m, result.Price);
        Assert.Equal(12.5, result.DistanceKM);
        Assert.Equal(30, result.DurationMin);
        Assert.Equal(1.5m, result.Multiplier);
        Assert.Same(route, result.Route);
    }

    [Fact]
    public async Task RequestQuote_NoRoutes_Throws()
    {
        _osrm.Setup(service => service.GetRouteAsync(
                It.IsAny<IReadOnlyList<LocationContract>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OSRMResponseContract { Routes = [] });
        var service = new QuoteService(_osrm.Object, _pricing.Object);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.RequestQuoteAsync(
            new QuoteRequestContract
            {
                Pickup = new LocationContract(10, 106),
                Destination = new LocationContract(11, 107)
            }, CancellationToken.None));

        _pricing.Verify(service => service.ProcessPrice(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<WeatherCondition>(), It.IsAny<decimal>()),
            Times.Never);
    }
}
