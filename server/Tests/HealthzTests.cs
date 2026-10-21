using System.Net;

namespace ResumeApp.Tests;

[Collection("env")]
public class HealthzTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Healthz_with_secret_returns_200()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-origin-secret", ApiFactory.Secret);

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Healthz_without_secret_is_rejected_403()
    {
        var response = await factory.CreateClient().GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Healthz_with_wrong_secret_is_rejected_403()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-origin-secret", "wrong");

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

