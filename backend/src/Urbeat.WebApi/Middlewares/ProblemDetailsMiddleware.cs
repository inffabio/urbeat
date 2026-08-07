using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Middlewares;

public sealed class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            if (context.Response.StatusCode >= 400 && !HasProblemDetailsContentType(context))
            {
                buffer.Seek(0, SeekOrigin.Begin);
                var existingBody = await new StreamReader(buffer).ReadToEndAsync();

                var problemDetails = CreateProblemDetails(context, existingBody);
                var json = JsonSerializer.Serialize(problemDetails, JsonOptions);

                context.Response.ContentType = "application/problem+json; charset=utf-8";
                context.Response.Body = originalBody;
                await context.Response.WriteAsync(json);
            }
            else
            {
                buffer.Seek(0, SeekOrigin.Begin);
                await buffer.CopyToAsync(originalBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in ProblemDetailsMiddleware.");
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/problem+json; charset=utf-8";

            var problem = CreateProblemDetails(context, null);
            problem.Detail = "An internal server error occurred.";
            problem.Status = 500;
            problem.Title = "Internal Server Error";

            var json = JsonSerializer.Serialize(problem, JsonOptions);
            context.Response.Body = originalBody;
            await context.Response.WriteAsync(json);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool HasProblemDetailsContentType(HttpContext context)
    {
        var ct = context.Response.ContentType;
        return ct != null && ct.StartsWith("application/problem+json", StringComparison.OrdinalIgnoreCase);
    }

    private static ProblemDetails CreateProblemDetails(HttpContext context, string? existingBody)
    {
        var statusCode = context.Response.StatusCode;
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Type = GetProblemTypeUri(statusCode),
            Title = GetDefaultTitle(statusCode),
            Instance = context.Request.Path,
            Extensions = { ["traceId"] = traceId }
        };

        if (!string.IsNullOrWhiteSpace(existingBody) && existingBody != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(existingBody);
                if (doc.RootElement.TryGetProperty("error", out var errorProp))
                {
                    problem.Detail = errorProp.GetString();
                }
                else if (doc.RootElement.TryGetProperty("message", out var messageProp))
                {
                    problem.Detail = messageProp.GetString();
                }
                else if (doc.RootElement.TryGetProperty("title", out var titleProp))
                {
                    problem.Title = titleProp.GetString();
                }

                if (doc.RootElement.TryGetProperty("code", out var codeProp))
                {
                    problem.Extensions["code"] = codeProp.GetString();
                }

                if (doc.RootElement.TryGetProperty("errors", out var errorsProp))
                {
                    // Clone the element so it survives the JsonDocument disposal below.
                    problem.Extensions["errors"] = errorsProp.Clone();
                }
            }
            catch
            {
                problem.Detail = existingBody.Length > 200 ? existingBody[..200] : existingBody;
            }
        }

        return problem;
    }

    private static string GetProblemTypeUri(int statusCode) => statusCode switch
    {
        400 => "https://httpstatuses.com/400",
        401 => "https://httpstatuses.com/401",
        403 => "https://httpstatuses.com/403",
        404 => "https://httpstatuses.com/404",
        409 => "https://httpstatuses.com/409",
        422 => "https://httpstatuses.com/422",
        423 => "https://httpstatuses.com/423",
        500 => "https://httpstatuses.com/500",
        _ => $"https://httpstatuses.com/{statusCode}"
    };

    private static string GetDefaultTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        423 => "Locked",
        500 => "Internal Server Error",
        _ => "Error"
    };
}
