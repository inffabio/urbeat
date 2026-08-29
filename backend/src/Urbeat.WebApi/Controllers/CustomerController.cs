using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Npgsql;
using Urbeat.Application.DTOs;
using Urbeat.Application.Security;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/customer")]
[Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
public sealed class CustomerController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public CustomerController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("home")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetHome()
    {
        return Ok(new
        {
            area = "customer",
            message = "Customer authorized."
        });
    }

    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCurrentCustomer(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var profile = await _dbContext.Users
            .Where(user => user.Id == userId.Value)
            .Select(user => new
            {
                FullName = _dbContext.UserClaims
                    .Where(claim => claim.UserId == user.Id && claim.ClaimType == "FullName")
                    .Select(claim => claim.ClaimValue)
                    .FirstOrDefault() ?? user.Email ?? string.Empty,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber,
                PrimaryAddressId = _dbContext.CustomerAddresses
                    .Where(address => address.UserId == user.Id && address.IsPrimary)
                    .OrderByDescending(address => address.CreatedAtUtc)
                    .Select(address => (Guid?)address.Id)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return profile is null ? Unauthorized() : Ok(profile);
    }

    [HttpPut("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCurrentCustomer(
        [FromBody] UpdateCustomerProfileRequestDto request,
        [FromServices] UserManager<IdentityUser<Guid>> userManager,
        [FromServices] IValidator<UpdateCustomerProfileRequestDto> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray())));
        }

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null) return Unauthorized();

        var email = request.Email.Trim().ToLowerInvariant();
        var fullName = request.FullName.Trim();
        var phone = request.PhoneNumber.Trim();

        // Friendly pre-check so a conflicting e-mail surfaces as a clear 409. The database
        // guarantees uniqueness through the unique UserNameIndex on NormalizedUserName: this
        // application always mirrors the e-mail into UserName, so a duplicate e-mail cannot be
        // persisted even under a race (NormalizedEmail only has a non-unique EmailIndex).
        var emailOwner = await userManager.FindByEmailAsync(email);
        if (emailOwner is not null && emailOwner.Id != user.Id)
        {
            return Conflict(new { error = "Este e-mail já está em uso." });
        }

        // Apply e-mail, username and phone through the UserManager so its validators, key
        // normalization and concurrency stamp stay in charge, and commit them together with the
        // FullName claim in a single relational transaction to avoid a partially persisted user.
        // InMemory (unit tests) does not support transactions and falls back to sequential saves.
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var setPhoneResult = await userManager.SetPhoneNumberAsync(user, phone);
            if (!setPhoneResult.Succeeded) return IdentityErrorResult(setPhoneResult);

            var setEmailResult = await userManager.SetEmailAsync(user, email);
            if (!setEmailResult.Succeeded) return IdentityErrorResult(setEmailResult);

            var setUserNameResult = await userManager.SetUserNameAsync(user, email);
            if (!setUserNameResult.Succeeded) return IdentityErrorResult(setUserNameResult);

            var claim = await _dbContext.UserClaims
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.ClaimType == "FullName", cancellationToken);
            if (claim is null)
            {
                _dbContext.UserClaims.Add(new IdentityUserClaim<Guid>
                {
                    UserId = user.Id,
                    ClaimType = "FullName",
                    ClaimValue = fullName,
                });
            }
            else
            {
                claim.ClaimValue = fullName;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new { error = "Este e-mail já está em uso." });
        }

        return await GetCurrentCustomer(cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        // PostgreSQL raises SQLSTATE 23505 (unique_violation) when a racing request persists the
        // same e-mail/username against the unique UserNameIndex on NormalizedUserName. Only this
        // provider-specific signal is mapped to a conflict; any other DbUpdateException (or a
        // non-unique failure) is left to propagate instead of being masked as a 409.
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }

    private IActionResult IdentityErrorResult(IdentityResult result)
    {
        if (result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return Conflict(new { error = "Este e-mail já está em uso." });
        }

        var messages = result.Errors
            .Select(error => error.Description)
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .ToArray();

        if (messages.Length == 0)
        {
            messages = ["Não foi possível atualizar o cadastro."];
        }

        return ValidationProblem(new ValidationProblemDetails(
            new Dictionary<string, string[]> { ["profile"] = messages }));
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
