using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Tests;

internal static class AgentTestClient
{
    public const string AllowedOrigin = "http://localhost:4200";

    public static async Task<HttpClient> CreateAuthenticatedClientAsync(this WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var pairRequest = new HttpRequestMessage(HttpMethod.Post, "/pair");
        pairRequest.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        pairRequest.Headers.TryAddWithoutValidation("X-Urbeat-Pair", "1");

        var pairResponse = await client.SendAsync(pairRequest);
        pairResponse.EnsureSuccessStatusCode();
        var pair = await pairResponse.Content.ReadFromJsonAsync<PairAgentResponse>();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Urbeat-Local-Token", pair!.Token);
        return client;
    }

    public static HttpRequestMessage WithAllowedOrigin(this HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        return request;
    }
}
