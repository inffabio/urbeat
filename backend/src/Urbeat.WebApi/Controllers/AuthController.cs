using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IValidator<LoginRequestDto> _validator;
    private readonly IValidator<RegisterUserRequestDto> _registrationValidator;
    private readonly IValidator<ConfirmEmailRequestDto> _confirmEmailValidator;
    private readonly IValidator<ResendEmailConfirmationRequestDto> _resendValidator;
    private readonly IValidator<ResetPasswordRequestDto> _resetPasswordValidator;
    private readonly IAuthService _authService;
    private readonly IEmailConfirmationService _emailConfirmationService;

    public AuthController(
        IValidator<LoginRequestDto> validator,
        IValidator<RegisterUserRequestDto> registrationValidator,
        IValidator<ConfirmEmailRequestDto> confirmEmailValidator,
        IValidator<ResendEmailConfirmationRequestDto> resendValidator,
        IValidator<ResetPasswordRequestDto> resetPasswordValidator,
        IAuthService authService,
        IEmailConfirmationService emailConfirmationService)
    {
        _validator = validator;
        _registrationValidator = registrationValidator;
        _confirmEmailValidator = confirmEmailValidator;
        _resendValidator = resendValidator;
        _resetPasswordValidator = resetPasswordValidator;
        _authService = authService;
        _emailConfirmationService = emailConfirmationService;
    }

    [HttpPost("register/customer")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterCustomer([FromBody] RegisterUserRequestDto request, CancellationToken cancellationToken)
    {
        var validationProblem = await ValidateRegistrationAsync(request, cancellationToken);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var result = await _authService.RegisterCustomerAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return BuildRegistrationErrorResult(result.Errors);
        }

        BusinessMetrics.NewUsers.Inc();
        return StatusCode(StatusCodes.Status201Created, new
        {
            userId = result.UserId,
            emailConfirmationPending = result.EmailConfirmationPending,
            message = "Cadastro realizado. Verifique seu e-mail para ativar sua conta."
        });
    }

    [HttpPost("register/seller")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterSeller([FromBody] RegisterUserRequestDto request, CancellationToken cancellationToken)
    {
        var validationProblem = await ValidateRegistrationAsync(request, cancellationToken);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var result = await _authService.RegisterSellerAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.DocumentAlreadyRegistered)
            {
                Response.ContentType = "application/problem+json";
                return Conflict(new
                {
                    errors = result.Errors,
                    documentAlreadyRegistered = true,
                    emailConfirmationPending = result.EmailConfirmationPending,
                    existingUserEmail = result.ExistingUserEmail
                });
            }
            return BuildRegistrationErrorResult(result.Errors);
        }

        BusinessMetrics.NewUsers.Inc();
        return StatusCode(StatusCodes.Status201Created, new
        {
            userId = result.UserId,
            emailConfirmationPending = result.EmailConfirmationPending,
            message = "Cadastro realizado. Verifique seu e-mail para ativar sua loja."
        });
    }

    [HttpPost("login/customer")]
    [ProducesResponseType<AuthTokenResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> LoginCustomer([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        return LoginByRoleAsync(request, "Customer", cancellationToken);
    }

    [HttpPost("login/seller")]
    [ProducesResponseType<AuthTokenResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> LoginSeller([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        return LoginByRoleAsync(request, "Seller", cancellationToken);
    }

    [HttpPost("login/admin")]
    [ProducesResponseType<AuthTokenResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> LoginAdmin([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        return LoginByRoleAsync(request, "Admin", cancellationToken);
    }

    [HttpPost("token")]
    [ProducesResponseType<AuthTokenResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GenerateToken([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        return LoginByRoleAsync(request, "Customer", cancellationToken);
    }

    private async Task<IActionResult> LoginByRoleAsync(LoginRequestDto request, string requiredRole, CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray());

            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var response = await _authService.LoginAsync(request, requiredRole, ipAddress, cancellationToken);
        if (!response.Succeeded)
        {
            if (response.IsLockedOut)
            {
                return StatusCode(StatusCodes.Status423Locked, new { error = response.Error });
            }

            if (response.IsEmailNotConfirmed)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = response.Error,
                    code = "EMAIL_NOT_CONFIRMED"
                });
            }

            if (response.IsForbidden)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = response.Error });
            }

            return Unauthorized();
        }

        SetRefreshCookie(response.Token!.RefreshToken, response.Token.RefreshTokenExpiresAtUtc);

        return Ok(response.Token);
    }

    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokenResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue("urbeat.refresh_token", out var refreshToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized();
        }

        var response = await _authService.RefreshAsync(refreshToken, cancellationToken);
        if (response is null)
        {
            return Unauthorized();
        }

        SetRefreshCookie(response.RefreshToken, response.RefreshTokenExpiresAtUtc);
        return Ok(response);
    }

    [HttpPost("email/confirm/{code}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmEmailByCode([FromRoute] string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) return BadRequest();

        var result = await _emailConfirmationService.ConfirmByShortCodeAsync(code, cancellationToken);
        if (result.UserNotFound)
        {
            return NotFound(new { error = "Usuário não encontrado." });
        }

        if (result.InvalidToken)
        {
            return BadRequest(new
            {
                error = "Link de confirmação inválido ou expirado.",
                details = result.Errors
            });
        }

        if (!result.Succeeded && !result.AlreadyConfirmed)
        {
            return BadRequest(new { error = "Falha ao confirmar e-mail.", details = result.Errors });
        }

        return Ok(new { Succeeded = result.Succeeded, AlreadyConfirmed = result.AlreadyConfirmed });
    }

    [HttpPost("email/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequestDto request, CancellationToken cancellationToken)
    {
        var validationResult = await _confirmEmailValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var result = await _emailConfirmationService.ConfirmAsync(request, cancellationToken);
        if (result.UserNotFound)
        {
            return NotFound(new { error = "Usuário não encontrado." });
        }

        if (result.InvalidToken)
        {
            return BadRequest(new
            {
                error = "Token inválido ou expirado.",
                errors = result.Errors
            });
        }

        return Ok(new
        {
            succeeded = result.Succeeded,
            alreadyConfirmed = result.AlreadyConfirmed,
            message = result.AlreadyConfirmed
                ? "E-mail já confirmado."
                : "E-mail confirmado com sucesso."
        });
    }

    [HttpPost("email/resend-confirmation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendEmailConfirmation([FromBody] ResendEmailConfirmationRequestDto request, CancellationToken cancellationToken)
    {
        var validationResult = await _resendValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var result = await _emailConfirmationService.ResendAsync(request, cancellationToken);

        // Privacy: always 200 OK regardless of whether the user exists.
        return Ok(new
        {
            succeeded = result.Succeeded,
            alreadyConfirmed = result.AlreadyConfirmed,
            message = "Se o e-mail estiver cadastrado, um link de confirmação foi enviado."
        });
    }

    private void SetRefreshCookie(string refreshToken, DateTime refreshTokenExpiresAtUtc)
    {
        Response.Cookies.Append("urbeat.refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = new DateTimeOffset(refreshTokenExpiresAtUtc),
            Path = "/"
        });
    }

    private async Task<IActionResult?> ValidateRegistrationAsync(RegisterUserRequestDto request, CancellationToken cancellationToken)
    {
        var validationResult = await _registrationValidator.ValidateAsync(request, cancellationToken);
        if (validationResult.IsValid)
        {
            return null;
        }

        return ValidationProblem(new ValidationProblemDetails(validationResult.Errors
            .GroupBy(x => x.PropertyName)
            .ToDictionary(
                keySelector: group => group.Key,
                elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
    }

    private IActionResult BuildRegistrationErrorResult(IReadOnlyCollection<string> errors)
    {
        Response.ContentType = "application/problem+json";
        if (errors.Any(x => x.Contains("already exists", StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict(new { errors });
        }

        return BadRequest(new { errors });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Informe um e-mail válido." });

        var found = await _authService.ForgotPasswordAsync(request, cancellationToken);
        return Ok(new { found, message = found ? "E-mail de recuperação enviado com sucesso." : "E-mail não encontrado em nossa base de dados." });
    }

    [HttpGet("validate-reset-token")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateResetToken([FromQuery] string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { valid = false, message = "Token é obrigatório." });

        var result = await _authService.ValidateResetTokenAsync(token, cancellationToken);
        return Ok(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request, CancellationToken cancellationToken)
    {
        var validationResult = await _resetPasswordValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validationResult.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray())));
        }

        var (succeeded, error) = await _authService.ResetPasswordAsync(request, cancellationToken);
        if (!succeeded)
            return BadRequest(new { message = error ?? "Não foi possível redefinir a senha." });

        return Ok(new { message = "Senha alterada com sucesso." });
    }

    [HttpPost("update-email")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateEmail([FromBody] UpdateEmailRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NewEmail) || string.IsNullOrWhiteSpace(request.CurrentEmail))
            return BadRequest(new { message = "Informe os e-mails." });

        var (succeeded, error) = await _authService.UpdateEmailAsync(request.UserId, request, cancellationToken);
        if (!succeeded)
            return BadRequest(new { message = error ?? "Não foi possível atualizar o e-mail." });

        return Ok(new { message = "E-mail atualizado. Um novo link de confirmação foi enviado." });
    }
}
