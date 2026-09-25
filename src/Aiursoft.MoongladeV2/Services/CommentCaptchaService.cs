using System.Security.Cryptography;
using Aiursoft.Scanner.Abstractions;
using Edi.Captcha;
using Microsoft.AspNetCore.DataProtection;

namespace Aiursoft.MoongladeV2.Services;

public class CommentCaptchaService(IDataProtectionProvider provider, IStatelessCaptcha captcha) : IScopedDependency
{
    private readonly ITimeLimitedDataProtector protector = provider
        .CreateProtector("Moonglade.CommentCaptcha.v1")
        .ToTimeLimitedDataProtector();

    public (string ImageBase64, string Token) Create(Guid documentId)
    {
        var result = captcha.GenerateCaptcha();
        var payload = $"{documentId:N}:{result.Token}";
        return (Convert.ToBase64String(result.ImageBytes), protector.Protect(payload, TimeSpan.FromMinutes(5)));
    }

    public bool Verify(Guid documentId, string? token, string? answer)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || string.IsNullOrWhiteSpace(answer) || answer.Length > 16)
            return false;

        try
        {
            var payload = protector.Unprotect(token);
            var parts = payload.Split(':', 2);
            return parts.Length == 2 &&
                   parts[0] == documentId.ToString("N") &&
                   captcha.Validate(answer, parts[1]);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
