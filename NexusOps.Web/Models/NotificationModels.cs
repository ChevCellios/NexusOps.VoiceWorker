using System.ComponentModel.DataAnnotations;
namespace NexusOps.Web.Models;
public enum DemoChannel { Call, Sms, Email, WhatsApp, Telegram, Instagram }
public sealed record DemoNotification(Guid Id,DemoChannel Channel,string Recipient,string Message,DateTimeOffset CreatedAt);
public sealed class CreateDemoNotificationInput{public DemoChannel Channel{get;set;}[Required]public string Recipient{get;set;}="";[Required][StringLength(1000)]public string Message{get;set;}="";}
