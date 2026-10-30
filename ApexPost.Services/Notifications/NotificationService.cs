using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;

namespace ApexPost.Services.Notifications;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> GetRecentAsync(BranchCode? branch, int take, CancellationToken cancellationToken);
}

public sealed class NotificationService(INotificationQueries queries) : INotificationService
{
    public Task<IReadOnlyList<NotificationDto>> GetRecentAsync(BranchCode? branch, int take, CancellationToken cancellationToken) =>
        queries.ListRecentAsync(branch, Math.Clamp(take, 1, 200), cancellationToken);
}
