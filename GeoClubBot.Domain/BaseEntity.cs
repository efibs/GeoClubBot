using MediatR;

namespace Entities;

public abstract class BaseEntity
{
    public void AddDomainEvent(INotification notification)
    {
        _domainEvents.Add(notification);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
    
    public IReadOnlyCollection<INotification> DomainEvents => _domainEvents.AsReadOnly();

    private readonly List<INotification> _domainEvents = [];
}