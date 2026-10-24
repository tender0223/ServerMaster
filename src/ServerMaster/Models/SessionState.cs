namespace ServerMaster.Models
{
    public enum SessionState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Reconnecting = 3,
        Failed = 4
    }
}
