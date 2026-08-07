using System.Net;
using System.Text;
using Hangfire.Dashboard;

namespace Urbeat.WebApi.Infrastructure;

/// <summary>
/// Filtro Basic Auth para o Hangfire Dashboard.
/// Credenciais vêm das chaves Hangfire:DashboardUser/DashboardPassword.
/// </summary>
public sealed class HangfireBasicAuthFilter : IDashboardAuthorizationFilter
{
    private readonly string _user;
    private readonly string _password;

    public HangfireBasicAuthFilter(string user, string password)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _password = password ?? throw new ArgumentNullException(nameof(password));
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        var header = httpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(header) && header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            var encoded = header["Basic ".Length..].Trim();
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var parts = decoded.Split(':', 2);
                if (parts.Length == 2 &&
                    string.Equals(parts[0], _user, StringComparison.Ordinal) &&
                    string.Equals(parts[1], _password, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // header malformado: cai no else abaixo
            }
        }

        httpContext.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Hangfire\"";
        httpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        return false;
    }
}
