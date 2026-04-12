using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nova.Presets.Api.Configuration;
using Nova.Shared.Data;

namespace Nova.Presets.Api.Endpoints;

/// <summary>
/// Upload avatar: <c>POST /api/v1/user-profile/avatar</c>
///
/// Accepts <c>multipart/form-data</c> — no JSON body.
/// <c>tenant_id</c> and <c>user_id</c> come from JWT claims only.
/// Saves file to <c>AvatarStorage.LocalDirectory/{tenantId}/{userId}.{ext}</c> and
/// updates <c>nova_auth.tenant_user_profile.avatar_url</c>.
/// </summary>
public static class UploadAvatarEndpoint
{
    private static readonly HashSet<string> AllowedMimeTypes =
        ["image/jpeg", "image/png", "image/webp"];

    private static readonly Dictionary<string, string> MimeToExt = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"]  = ".png",
        ["image/webp"] = ".webp",
    };

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/user-profile/avatar", HandleAsync)
             .RequireAuthorization()
             .DisableAntiforgery()
             .WithName("UploadAvatar")
             .Accepts<IFormFile>("multipart/form-data");
    }

    private static async Task<IResult> HandleAsync(
        [FromForm] IFormFile?             avatar,
        HttpContext                        httpContext,
        IDbConnectionFactory               connectionFactory,
        IOptions<AuthDbSettings>           authDbOptions,
        IOptions<AvatarStorageSettings>    avatarOptions,
        CancellationToken                  ct)
    {
        // Validate file presence and type
        if (avatar is null || avatar.Length == 0)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["avatar"] = ["avatar file is required."] },
                title: "Validation failed");

        string contentType = avatar.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!AllowedMimeTypes.Contains(contentType))
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["avatar"] = ["File must be a JPEG, PNG, or WebP image."] },
                title: "Validation failed");

        if (avatar.Length > MaxFileSizeBytes)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["avatar"] = ["File must be 5 MB or smaller."] },
                title: "Validation failed");

        // Extract identity from JWT — no JSON body for this endpoint
        string? tenantId = httpContext.User.FindFirstValue("tenant_id");
        string? userId   = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
            return TypedResults.Problem(
                title:      "Unauthorized",
                detail:     "Unable to resolve tenant_id or user_id from token.",
                statusCode: StatusCodes.Status401Unauthorized);

        AvatarStorageSettings storage = avatarOptions.Value;
        string ext      = MimeToExt[contentType];
        string tenantDir = Path.Combine(storage.LocalDirectory, tenantId);
        string filePath  = Path.Combine(tenantDir, $"{userId}{ext}");

        // Ensure directory exists
        Directory.CreateDirectory(tenantDir);

        // Write file (overwrite if same user re-uploads same extension)
        await using (FileStream fs = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await avatar.CopyToAsync(fs, ct);
        }

        string avatarUrl = $"{storage.PublicBaseUrl.TrimEnd('/')}/{tenantId}/{userId}{ext}";

        // Update avatar_url in nova_auth.tenant_user_profile
        AuthDbSettings authDb     = authDbOptions.Value;
        ISqlDialect    dialect    = PresetsDbHelper.Dialect(authDb.DbType);
        string         profile    = dialect.TableRef("nova_auth", "tenant_user_profile");

        using IDbConnection conn = connectionFactory.CreateFromConnectionString(
            authDb.ConnectionString, authDb.DbType);

        int rows = await conn.ExecuteAsync(
            $"""
            UPDATE {profile}
            SET    avatar_url = @AvatarUrl,
                   updated_on = @Now,
                   updated_by = @UserId,
                   updated_at = 'Nova.Presets.Api'
            WHERE  tenant_id = @TenantId AND user_id = @UserId
            """,
            new { AvatarUrl = avatarUrl, Now = PresetsDbHelper.UtcNow(), TenantId = tenantId, UserId = userId },
            commandTimeout: 10);

        if (rows == 0)
            return TypedResults.Problem(
                title:      "Not found",
                detail:     "User profile not found.",
                statusCode: StatusCodes.Status404NotFound);

        return TypedResults.Ok(new { avatar_url = avatarUrl });
    }
}
