using System.Security.Cryptography;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Aiursoft.MoongladeV2.Services;

public class CommentCaptchaService(IDataProtectionProvider provider) : IScopedDependency
{
    private readonly ITimeLimitedDataProtector protector = provider
        .CreateProtector("Moonglade.CommentCaptcha.v1")
        .ToTimeLimitedDataProtector();

    public (string Question, string Token) Create(Guid documentId)
    {
        var first = RandomNumberGenerator.GetInt32(2, 10);
        var second = RandomNumberGenerator.GetInt32(1, 10);
        var payload = $"{documentId:N}:{first + second}";
        return ($"{first} + {second} = ?", protector.Protect(payload, TimeSpan.FromMinutes(30)));
    }

    public bool Verify(Guid documentId, string? token, string? answer)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || !int.TryParse(answer, out var supplied))
            return false;

        try
        {
            var payload = protector.Unprotect(token);
            var parts = payload.Split(':', 2);
            return parts.Length == 2 &&
                   parts[0] == documentId.ToString("N") &&
                   int.TryParse(parts[1], out var expected) &&
                   supplied == expected;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
