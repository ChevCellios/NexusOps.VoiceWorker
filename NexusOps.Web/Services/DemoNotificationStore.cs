using NexusOps.Web.Models;
namespace NexusOps.Web.Services;
public interface IDemoNotificationStore{IReadOnlyList<DemoNotification> List();void Send(CreateDemoNotificationInput input);}
public sealed class DemoNotificationStore:IDemoNotificationStore{readonly List<DemoNotification> list=[];public IReadOnlyList<DemoNotification> List()=>list.OrderByDescending(x=>x.CreatedAt).ToArray();public void Send(CreateDemoNotificationInput x){if(string.IsNullOrWhiteSpace(x.Recipient)||string.IsNullOrWhiteSpace(x.Message))throw new ArgumentException("Primatelj i poruka su obavezni.");list.Add(new(Guid.NewGuid(),x.Channel,x.Recipient.Trim(),x.Message.Trim(),DateTimeOffset.UtcNow));}}
