namespace Urbeat.Application.Security;

/// <summary>
/// Stable error codes for the administrative store context. Messages stay generic; a code never
/// confirms whether another administrator or store exists.
/// </summary>
public static class AdminStoreErrorCodes
{
    public const string StoreNotFound = "STORE_NOT_FOUND";
    public const string StoreOwnerNotFound = "STORE_OWNER_NOT_FOUND";
    public const string ContextNotAllowed = "ADMIN_STORE_CONTEXT_NOT_ALLOWED";
    public const string ContextInvalid = "ADMIN_STORE_CONTEXT_INVALID";
    public const string ContextExpired = "ADMIN_STORE_CONTEXT_EXPIRED";
    public const string ContextAdminNotAllowed = "ADMIN_STORE_CONTEXT_ADMIN_NOT_ALLOWED";
    public const string ContextStoreMismatch = "ADMIN_STORE_CONTEXT_STORE_MISMATCH";
    public const string ContextStoreMissing = "ADMIN_STORE_CONTEXT_STORE_MISSING";
    public const string ActionNotAllowed = "ADMIN_STORE_ACTION_NOT_ALLOWED";

    public static string MessageFor(string code) => code switch
    {
        StoreNotFound => "Loja não encontrada.",
        StoreOwnerNotFound => "Loja sem proprietário resolvível.",
        ContextNotAllowed => "Este acesso administrativo não é permitido.",
        ContextAdminNotAllowed => "A conta perdeu o acesso administrativo.",
        ContextExpired => "Sessão administrativa expirada.",
        ContextStoreMismatch => "O contexto administrativo pertence a outra loja.",
        ContextStoreMissing => "A loja deste contexto não existe mais.",
        ActionNotAllowed => "Ação não permitida no modo administrador.",
        _ => "Contexto administrativo inválido."
    };
}
