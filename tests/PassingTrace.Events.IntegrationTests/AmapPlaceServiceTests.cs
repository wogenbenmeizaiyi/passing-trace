using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Places;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AmapPlaceServiceTests
{
    [Fact]
    public async Task SearchAsync_MapsAmapPoiToGcj02Candidate()
    {
        var handler = new StubHandler("""
            {"status":"1","pois":[{"id":"B001","name":"西湖风景名胜区","address":"西湖区",
            "pname":"浙江省","cityname":"杭州市","adname":"西湖区","adcode":"330106",
            "type":"风景名胜","location":"120.143,30.249","distance":"230"}]}
            """);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://restapi.amap.com") };
        var service = new AmapPlaceService(client, Options.Create(new AmapOptions { WebServiceKey = "test-key" }));

        var result = await service.SearchAsync(new PlaceSearchRequest("nearby", null, 30.25m, 120.14m, 1000, null),
            CancellationToken.None);

        var place = Assert.Single(result);
        Assert.Equal("B001", place.PoiId);
        Assert.Equal("GCJ02", place.CoordinateSystem);
        Assert.Equal(230, place.DistanceMeters);
        Assert.Contains("/v3/place/around", handler.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task SearchAsync_RejectsMissingCoordinatesAndMissingKey()
    {
        var client = new HttpClient(new StubHandler("{}")) { BaseAddress = new Uri("https://restapi.amap.com") };
        var service = new AmapPlaceService(client, Options.Create(new AmapOptions { WebServiceKey = "key" }));
        await Assert.ThrowsAsync<DomainValidationException>(() => service.SearchAsync(
            new PlaceSearchRequest("nearby", null, null, null, null, null), CancellationToken.None));
        var noKey = new AmapPlaceService(client, Options.Create(new AmapOptions()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => noKey.SearchAsync(
            new PlaceSearchRequest("keyword", "西湖", null, null, null, null), CancellationToken.None));
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
