using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace NexusOps.VoiceWorker.Models;

public enum VoiceCallStatus
{
    Queued, Initiating, Ringing, Answered, InProgress, Completed, Failed, Busy, NoAnswer, Cancelled
}

public sealed record VoiceCallSession(
    Guid Id,
    Guid TenantId,
    Guid? OrganizationId,
    Guid AgentTaskId,
    Guid AgentId,
    string CallDirection,
    VoiceCallStatus Status,
    string Provider,
    string? ProviderCallId,
    string? FromNumber,
    string? ToNumber,
    string? ContactName,
    string LanguageCode,
    string? Purpose,
    string? InitialInstruction,
    bool RecordingEnabled,
    bool ConsentRequired,
    bool ConsentObtained,
    DateTimeOffset? StartedAt,
    DateTimeOffset? AnsweredAt,
    DateTimeOffset? EndedAt,
    int? DurationSeconds,
    string? Outcome,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StartVoiceCallRequest(
    [Required] Guid VoiceCallSessionId,
    [Required] Guid AgentTaskId);

public sealed record StartVoiceCallResponse(
    Guid VoiceCallSessionId,
    string Status,
    string Provider,
    string? ProviderCallId);

public sealed record ProviderStatusRequest(
    [Required] string ProviderCallId,
    [Required] string Status,
    Guid? VoiceCallSessionId);

public sealed class TwilioStatusWebhookRequest
{
    [FromForm(Name = "CallSid")]
    public string CallSid { get; init; } = string.Empty;

    [FromForm(Name = "CallStatus")]
    public string CallStatus { get; init; } = string.Empty;
}

public sealed record ProviderAnswerRequest(Guid VoiceCallSessionId);
public sealed record ProviderAnswerResponse(Guid VoiceCallSessionId, string MediaStreamUrl);
public sealed record CompleteVoiceCallRequest([StringLength(2000)] string? Summary);
public sealed record FailVoiceCallRequest([Required, StringLength(1000)] string Reason);
public sealed record VoiceCallOutcomeRequest(
    [Required, StringLength(1000)] string Outcome,
    [StringLength(2000)] string? Notes);
