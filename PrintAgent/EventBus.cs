namespace PrintAgent
{
    public static class EventBus
    {
        // Tetiklendiğinde UI tarafında (Program.cs) ikon rengini değiştirir
        public static event Action<bool>? ConnectionStateChanged;
        
        // Tetiklendiğinde UI tarafında (Program.cs) bildirim baloncuğu gösterir
        public static event Action<string, string>? ActivityLogged;

        public static bool IsConnected { get; private set; }

        public static void NotifyConnectionState(bool isConnected)
        {
            IsConnected = isConnected;
            ConnectionStateChanged?.Invoke(isConnected);
        }

        public static void NotifyActivity(string title, string message)
        {
            ActivityLogged?.Invoke(title, message);
        }

        public static event Action? ForceReconnectRequested;

        public static void RequestForceReconnect()
        {
            ForceReconnectRequested?.Invoke();
        }

        public static bool IsPaused { get; private set; }
        public static event Action<bool>? PauseStateChanged;

        public static void SetPauseState(bool isPaused)
        {
            IsPaused = isPaused;
            PauseStateChanged?.Invoke(isPaused);
        }
    }
}
