namespace Urbeat.Application.DTOs;

public sealed class CreateCuisineTypeResultDto
{
    public CuisineTypeResponseDto? CuisineType { get; init; }

    public bool Created { get; init; }

    // Nome vazio/inválido para criação.
    public bool Invalid { get; init; }

    // Loja inexistente ou sem vínculo com o proprietário informado.
    public bool NotFound { get; init; }

    // Nome de categoria padrão, duplicado no escopo ou corrida no índice único.
    public bool Conflict { get; init; }
}
