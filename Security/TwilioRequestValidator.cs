using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Providers.Twilio;

namespace NexusOps.VoiceWorker.Security;

public interface ITwilioRequestValidator
{
    Task<bool> IsValidAsync(HttpRequest request, CancellationToken cancellationToken, string? externallyVisibleUrl = null);
}

public sealed class TwilioRequestValidator(IOptions<TwilioOptions> options) : ITwilioRequestValidator
{
    private readonly TwilioOptions _options = options.Value;

    public async Task<bool> IsValidAsync(
        HttpRequest request,
        CancellationToken cancellationToken,
        string? externallyVisibleUrl = null)
    {
        if (!request.Headers.TryGetValue("X-Twilio-Signature", out var signature) ||
            string.IsNullOrWhiteSpace(_options.AuthToken) ||
            _options.AuthToken.StartsWith("YOUR_", StringComparison.Ordinal))
            return false;

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
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
