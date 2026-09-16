using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Providers.Twilio;
using NexusOps.VoiceWorker.Security;
using Xunit;

namespace NexusOps.Web.Tests;

public sealed class TwilioRequestValidatorTests
{
    private const string AuthToken = "test-auth-token";
    private const string PublicUrl = "https://voice.example.test/voice/provider/status?voiceCallSessionId=1d91cc7a-fb6f-48a0-9e36-a23f1ad730f6";

    [Fact]
    public async Task IsValidAsync_AcceptsMatchingSignature()
    {
        var request = CreateRequest("CA123", "completed");
        request.Headers["X-Twilio-Signature"] = CreateSignature(
            PublicUrl, ("CallSid", "CA123"), ("CallStatus", "completed"));

        var result = await CreateValidator().IsValidAsync(request, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsValidAsync_RejectsSignatureAfterFormIsChanged()
    {
        var request = CreateRequest("CA123", "failed");
        request.Headers["X-Twilio-Signature"] = CreateSignature(
            PublicUrl, ("CallSid", "CA123"), ("CallStatus", "completed"));

        var result = await CreateValidator().IsValidAsync(request, CancellationToken.None);

        Assert.False(result);
    }

    private static TwilioRequestValidator CreateValidator() => new(
        Options.Create(new TwilioOptions
        {
            AuthToken = AuthToken,
            PublicBaseUrl = "https://voice.example.test",
            ValidateSignatures = true
        }),
        NullLogger<TwilioRequestValidator>.Instance);

    private static HttpRequest CreateRequest(string callSid, string status)
    {
        var context = new DefaultHttpContext();
        var request = context.Request;
        request.Method = HttpMethods.Post;
        request.Path = "/voice/provider/status";
        request.QueryString = new QueryString("?voiceCallSessionId=1d91cc7a-fb6f-48a0-9e36-a23f1ad730f6");
        var body = $"CallSid={Uri.EscapeDataString(callSid)}&CallStatus={Uri.EscapeDataString(status)}";
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        request.ContentType = "application/x-www-form-urlencoded";
        request.ContentLength = request.Body.Length;
        return request;
    }

    private static string CreateSignature(string url, params (string Key, string Value)[] values)
    {
        var signedValue = new StringBuilder(url);
        foreach (var pair in values.OrderBy(item => item.Key, StringComparer.Ordinal))
            signedValue.Append(pair.Key).Append(pair.Value);
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(AuthToken));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedValue.ToString())));
    }
}
