namespace NovelM_App.Infrastructure.SignalR;

internal sealed class HubEnvelope<T>
{
    public bool Success { get; set; }

    public T? Response { get; set; }

    public int Status { get; set; }

    public string? Msg { get; set; }
}
