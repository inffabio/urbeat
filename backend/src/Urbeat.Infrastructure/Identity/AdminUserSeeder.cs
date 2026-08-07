using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Identity;

public sealed class AdminUserSeeder
{
    private static readonly string[] Roles = ["Admin", "Seller", "Customer"];

    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly IOptions<AdminSeedOptions> _options;
    private readonly ILogger<AdminUserSeeder> _logger;

    public AdminUserSeeder(
        UserManager<IdentityUser<Guid>> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<AdminSeedOptions> options,
        ILogger<AdminUserSeeder> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _options = options;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var roleName in Roles)
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await _roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                if (!roleResult.Succeeded)
                {
                    _logger.LogWarning("Failed to create role {RoleName}.", roleName);
                }
            }
        }

        var adminEmail = _options.Value.Email.Trim().ToLowerInvariant();
        var existingAdmin = await _userManager.FindByEmailAsync(adminEmail);
        if (existingAdmin is not null)
        {
            if (!await _userManager.IsInRoleAsync(existingAdmin, "Admin"))
            {
                await _userManager.AddToRoleAsync(existingAdmin, "Admin");
            }

            return;
        }

        var admin = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };

        var createResult = await _userManager.CreateAsync(admin, _options.Value.Password);
        if (!createResult.Succeeded)
        {
            var reason = string.Join("; ", createResult.Errors.Select(x => x.Description));
            _logger.LogWarning("Unable to seed admin user: {Reason}", reason);
            return;
        }

        await _userManager.AddToRoleAsync(admin, "Admin");
    }
}