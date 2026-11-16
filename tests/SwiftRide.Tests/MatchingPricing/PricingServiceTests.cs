using MatchingPricing_Service.Application.Services.Impl;
using MatchingPricing_Service.Application.Services.Pricing;
using MatchingPricing_Service.Application.Services.Util;
using MatchingPricing_Service.Domain;
using Microsoft.Extensions.Options;
using Moq;

namespace SwiftRide.Tests.MatchingPricing;

public sealed class PricingServiceTests
{
    private readonly Mock<IPricingStrategy> _normal = new();
    private readonly Mock<IPricingStrategy> _rain = new();

    public PricingServiceTests()
    {
        _normal.SetupGet(strategy => strategy.WeatherCondition).Returns(WeatherCondition.Normal);
        _normal.SetupGet(strategy => strategy.Multiplier).Returns(1m);
        _rain.SetupGet(strategy => strategy.WeatherCondition).Returns(WeatherCondition.Rain);
        _rain.SetupGet(strategy => strategy.Multiplier).Returns(1.5m);
    }

    [Fact]
    public void ProcessPrice_DelegatesToMatchingStrategy()
    {
        _rain.Setup(strategy => strategy.Calculate(new PricingContext(12.5m, 30m, 0.8m)))
            .Returns(125_000m);
        var service = CreateService();

        var result = service.ProcessPrice(12.5, 30, WeatherCondition.Rain, 0.8m);

        Assert.Equal(125_000m, result);
        _rain.Verify(strategy => strategy.Calculate(new PricingContext(12.5m, 30m, 0.8m)), Times.Once);
        _normal.Verify(strategy => strategy.Calculate(It.IsAny<PricingContext>()), Times.Never);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ProcessPrice_InvalidDistance_Throws(double distance)
    {
        var service = CreateService();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.ProcessPrice(distance, 10, WeatherCondition.Normal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void ProcessPrice_InvalidDuration_Throws(double duration)
    {
        var service = CreateService();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.ProcessPrice(10, duration, WeatherCondition.Normal));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void ProcessPrice_InvalidPromotionMultiplier_Throws(double multiplier)
    {
        var service = CreateService();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.ProcessPrice(10, 20, WeatherCondition.Normal, (decimal)multiplier));
    }

    [Fact]
    public void ProcessPrice_NegativeStrategyResult_Throws()
    {
        _normal.Setup(strategy => strategy.Calculate(It.IsAny<PricingContext>())).Returns(-1m);
        var service = CreateService();

        Assert.Throws<InvalidOperationException>(() =>
            service.ProcessPrice(10, 20, WeatherCondition.Normal));
    }

    [Fact]
    public void ProcessPrice_UnsupportedWeather_Throws()
    {
        var service = CreateService();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.ProcessPrice(10, 20, (WeatherCondition)999));
    }

    [Theory]
    [InlineData(WeatherCondition.Normal, 1)]
    [InlineData(WeatherCondition.Rain, 1.5)]
    public void GetMultiplier_ReturnsConfiguredStrategyValue(WeatherCondition weather, double expected)
    {
        var service = CreateService();

        Assert.Equal((decimal)expected, service.GetMultiplier(weather));
    }

    private PricingService CreateService() => new([_normal.Object, _rain.Object]);
}

public sealed class PricingStrategyTests
{
    private static readonly PricingOptions Options = new()
    {
        PricePerKilometer = 10_000m,
        PricePerMinute = 1_000m,
        Toll = 5_000m,
        TaxRate = 0.1m,
        NormalMultiplier = 1m,
        RainMultiplier = 1.5m
    };

    [Fact]
    public void NormalStrategy_CalculatesSubtotalTollPromotionAndTax()
    {
        var strategy = new NormalPricingStrategy(Microsoft.Extensions.Options.Options.Create(Options));

        var result = strategy.Calculate(new PricingContext(10m, 20m, 0.8m));

        Assert.Equal(110_000m, result);
        Assert.Equal(1m, strategy.Multiplier);
        Assert.Equal(WeatherCondition.Normal, strategy.WeatherCondition);
    }

    [Fact]
    public void RainStrategy_AppliesRainMultiplier()
    {
        var strategy = new RainPricingStrategy(Microsoft.Extensions.Options.Options.Create(Options));

        var result = strategy.Calculate(new PricingContext(10m, 20m, 1m));

        Assert.Equal(203_500m, result);
        Assert.Equal(1.5m, strategy.Multiplier);
        Assert.Equal(WeatherCondition.Rain, strategy.WeatherCondition);
    }
}
