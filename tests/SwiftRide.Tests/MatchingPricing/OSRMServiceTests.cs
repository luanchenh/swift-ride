using System.Net;
using MatchingPricing_Service.Application.Contracts;
using MatchingPricing_Service.Application.Services.Util;
using SwiftRide.Tests.Support;

namespace SwiftRide.Tests.MatchingPricing;

public sealed class OSRMServiceTests
{
    [Fact]
    public async Task GetRoute_ValidLocations_BuildsInvariantUrlAndDeserializesResponse()
    {
        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"code":"Ok","routes":[{"distance":12500,"duration":1800,"geometry":{"type":"LineString","coordinates":[]}}],"waypoints":[]}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        });
        var service = new OSRMService(new HttpClient(handler));

        var result = await service.GetRouteAsync(
            [new LocationContract(10.5, 106.25), new LocationContract(11.75, 107.5)]);

        Assert.Equal("Ok", result.Code);
        Assert.Equal(12_500, Assert.Single(result.Routes).Distance);
        Assert.NotNull(requestedUri);
        Assert.Contains("106.25,10.5;107.5,11.75", requestedUri.AbsoluteUri);
        Assert.Contains("geometries=geojson", requestedUri.Query);
    }

    [Fact]
    public async Task GetRoute_NullLocations_Throws()
    {
        var service = new OSRMService(new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.GetRouteAsync(null!));
    }

    [Fact]
    public async Task GetRoute_FewerThanTwoLocations_ThrowsWithoutHttpRequest()
    {
        var called = false;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            called = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var service = new OSRMService(new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetRouteAsync([new LocationContract(10, 106)]));

        Assert.False(called);
    }

    [Theory]
    [InlineData(-91, 106)]
    [InlineData(91, 106)]
    [InlineData(double.NaN, 106)]
    [InlineData(10, -181)]
    [InlineData(10, 181)]
    [InlineData(10, double.PositiveInfinity)]
    public async Task GetRoute_InvalidLocation_Throws(double latitude, double longitude)
    {
        var service = new OSRMService(new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetRouteAsync(
            [new LocationContract(latitude, longitude), new LocationContract(10, 106)]));
    }

    [Fact]
    public async Task GetRoute_ErrorStatus_ThrowsHttpRequestException()
    {
        var service = new OSRMService(StubHttpMessageHandler.JsonClient(
            "{}", HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetRouteAsync(
            [new LocationContract(10, 106), new LocationContract(11, 107)]));
    }

    [Fact]
    public async Task GetRoute_EmptyBody_ThrowsHttpRequestException()
    {
        var service = new OSRMService(StubHttpMessageHandler.JsonClient("null"));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetRouteAsync(
            [new LocationContract(10, 106), new LocationContract(11, 107)]));
    }
}
