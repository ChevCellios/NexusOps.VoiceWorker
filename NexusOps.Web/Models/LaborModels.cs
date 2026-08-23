using System.ComponentModel.DataAnnotations;
namespace NexusOps.Web.Models;
public sealed record LaborEmployee(Guid Id,string Name);
public sealed record WorkOrderLaborEntry(string EmployeeName,DateTimeOffset StartedAt,DateTimeOffset EndedAt,decimal HourlyRate,string? Note){public decimal Hours=>(decimal)(EndedAt-StartedAt).TotalHours;public decimal Cost=>Hours*HourlyRate;}
public sealed class CreateLaborInput{[Required]public Guid EmployeeId{get;set;}[Required]public DateTimeOffset StartedAt{get;set;}[Required]public DateTimeOffset EndedAt{get;set;}[Range(typeof(decimal),"0","999999")]public decimal HourlyRate{get;set;}[StringLength(500)]public string? Note{get;set;}}
