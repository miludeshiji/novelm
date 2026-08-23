using Microsoft.AspNetCore.SignalR.Client;

namespace NovelM_App.Infrastructure.SignalR;

internal sealed class SignalRRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        return retryContext.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(5),
            2 => TimeSpan.FromSeconds(10),
            3 => TimeSpan.FromSeconds(20),
            _ => TimeSpan.FromSeconds(30)
        };
    }
}
