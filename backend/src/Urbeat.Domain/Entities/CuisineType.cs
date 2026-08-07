namespace Urbeat.Domain.Entities;

public sealed class CuisineType : BaseEntity
{
    public CuisineType() { }

    public CuisineType(Guid id, string name) : base(id)
    {
        Name = name;
        IsActive = true;
    }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Relacionamento 1:N com as Lojas (Validar se a categoria está em uso)
    public ICollection<Store> Stores { get; set; } = new List<Store>();
}