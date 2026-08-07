using System.Net.Http.Json;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace Urbeat.Infrastructure.Services;

public sealed class ViaCepService : IViaCepService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ViaCepService> _logger;

    public ViaCepService(HttpClient httpClient, ILogger<ViaCepService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ViaCepAddressResponseDto?> LookupAsync(string cep, CancellationToken cancellationToken = default)
    {
        var digits = new string(cep.Where(char.IsDigit).ToArray());
        if (digits.Length != 8)
        {
            return null;
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync($"/ws/{digits}/json/", cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "ViaCEP circuit is open for CEP lookup {Cep}.", digits);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "ViaCEP request failed for CEP lookup {Cep}.", digits);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "ViaCEP request timed out for CEP lookup {Cep}.", digits);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<ViaCepPayload>(cancellationToken: cancellationToken);
        if (payload is null || payload.Erro is not null)
        {
            return null;
        }

        return new ViaCepAddressResponseDto
        {
            Cep = digits,
            Street = payload.Logradouro ?? string.Empty,
            Neighborhood = payload.Bairro ?? string.Empty,
            City = payload.Localidade ?? string.Empty,
            State = payload.Uf ?? string.Empty,
            Complement = payload.Complemento
        };
    }

    private sealed class ViaCepPayload
    {
        public string? Logradouro { get; init; }

        public string? Bairro { get; init; }

        public string? Localidade { get; init; }

        public string? Uf { get; init; }

        public string? Complemento { get; init; }

        public string? Erro { get; init; }
    }
}
