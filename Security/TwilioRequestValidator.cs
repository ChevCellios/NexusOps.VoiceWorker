using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Providers.Twilio;

namespace NexusOps.VoiceWorker.Security;

public interface ITwilioRequestValidator
{
    Task<bool> IsValidAsync(HttpRequest request, CancellationToken cancellationToken, string? externallyVisibleUrl = null);
}

public sealed class TwilioRequestValidator(
    IOptions<TwilioOptions> options,
    ILogger<TwilioRequestValidator> logger) : ITwilioRequestValidator
{
    private readonly TwilioOptions _options = options.Value;

    public async Task<bool> IsValidAsync(
        HttpRequest request,
        CancellationToken cancellationToken,
        string? externallyVisibleUrl = null)
    {
        if (!_options.ValidateSignatures)
        {
            logger.LogWarning("Twilio signature validation is disabled for {Path}; use this only for a controlled test.", request.Path);
            return true;
        }

        if (!request.Headers.TryGetValue("X-Twilio-Signature", out var signature))
        {
            logger.LogWarning("Twilio request rejected: X-Twilio-Signature is missing for {Path}.", request.Path);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.AuthToken) ||
            _options.AuthToken.StartsWith("YOUR_", StringComparison.Ordinal))
        {
            logger.LogWarning("Twilio request rejected: AuthToken is not configured.");
            return false;
        }

        var publicUrl = externallyVisibleUrl ??
            $"{_options.PublicBaseUrl.TrimEnd('/')}{request.Path}{request.QueryString}";
        var signedValue = new StringBuilder(publicUrl);
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            foreach (var pair in form.OrderBy(item => item.Key, StringComparer.Ordinal))
                foreach (var value in pair.Value.OrderBy(item => item, StringComparer.Ordinal))
                    signedValue.Append(pair.Key).Append(value);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_options.AuthToken));
        var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedValue.ToString())));
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var suppliedBytes = Encoding.ASCII.GetBytes(signature.ToString());
        var valid = expectedBytes.Length == suppliedBytes.Length &&
                    CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
        if (!valid)
            logger.LogWarning("Twilio signature validation failed for public URL {PublicUrl}.", publicUrl);
        return valid;
    }
}
