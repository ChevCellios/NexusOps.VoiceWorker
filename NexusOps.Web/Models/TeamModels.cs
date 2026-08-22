namespace NexusOps.Web.Models;

public sealed record TeamMember(Guid EmployeeId, string Name, string JobTitle, string Status, string? Location, string? Task, bool DoNotDisturb, DateTimeOffset? StartedAt, string WorkMode, DateTimeOffset? ClockedInAt);
