using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface ICuisineTypeService
{
    // Lista pública: somente as categorias padrão protegidas (globais e ativas).
    Task<IReadOnlyCollection<CuisineTypeResponseDto>> GetActiveAsync(CancellationToken cancellationToken = default);

    // Lista autenticada: padrões protegidos + categorias privadas da loja do proprietário.
    Task<IReadOnlyCollection<CuisineTypeResponseDto>> GetForStoreAsync(Guid ownerUserId, Guid storeId, CancellationToken cancellationToken = default);

    // Cria uma categoria privada da loja. Nunca cria categoria global.
    // Retorna um resultado controlado para nome vazio, nome de categoria padrão, duplicado no escopo,
    // loja sem proprietário ou corrida no índice único (conflito), nunca deixando a exceção escapar.
    Task<CreateCuisineTypeResultDto> CreateForStoreAsync(Guid ownerUserId, Guid storeId, string name, CancellationToken cancellationToken = default);

    // Remove somente uma categoria privada da loja que não esteja em uso pela loja.
    Task<DeleteCuisineTypeResultDto> DeleteForStoreAsync(Guid ownerUserId, Guid storeId, Guid cuisineTypeId, CancellationToken cancellationToken = default);
}
