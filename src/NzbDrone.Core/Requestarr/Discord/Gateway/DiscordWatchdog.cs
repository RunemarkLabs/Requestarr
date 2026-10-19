using System;

namespace Requestarr.Core.Discord.Gateway
{
    // Ends the process when the gateway stays disconnected too long or heartbeats stop, so
    // the container restarts with a fresh connection (Docker restart policy).
    public class DiscordWatchdog
    {
        public static readonly TimeSpan DisconnectLimit = TimeSpan.FromMinutes(10);

        // Discord heartbeats roughly every 41 seconds; five minutes without an acknowledged
        // heartbeat while "connected" means the connection is dead.
        public static readonly TimeSpan HeartbeatLimit = TimeSpan.FromMinutes(5);

        private readonly Func<DateTime> _clock;
        private readonly Action<string> _terminate;
        private readonly object _lock = new();

        private bool _connected;
        private DateTime _disconnectedSince;
        private DateTime _lastHeartbeat;
        private bool _terminated;

        public DiscordWatchdog(Func<DateTime> clock, Action<string> terminate)
        {
            _clock = clock;
            _terminate = terminate;
            _disconnectedSince = clock();
            _lastHeartbeat = clock();
        }

        public bool Connected
        {
            get
            {
                lock (_lock)
                {
                    return _connected;
                }
            }
        }

        public void OnConnected()
        {
            lock (_lock)
            {
                _connected = true;
                _lastHeartbeat = _clock();
            }
        }

        public void OnDisconnected()
        {
            lock (_lock)
            {
                if (_connected)
                {
                    _disconnectedSince = _clock();
                }

                _connected = false;
            }
        }

        public void OnHeartbeat()
        {
            lock (_lock)
            {
                _lastHeartbeat = _clock();
            }
        }

        // Called periodically. Returns the reason when it ended the process.
        public string Check()
        {
            string reason = null;

            lock (_lock)
            {
                if (_terminated)
                {
                    return null;
                }

                var now = _clock();

                if (!_connected && now - _disconnectedSince >= DisconnectLimit)
                {
                    reason = $"Discord has been disconnected for {(now - _disconnectedSince).TotalMinutes:0} minutes";
                }
                else if (_connected && now - _lastHeartbeat >= HeartbeatLimit)
                {
                    reason = $"No Discord heartbeat for {(now - _lastHeartbeat).TotalMinutes:0} minutes";
                }

                if (reason != null)
                {
                    _terminated = true;
                }
            }

            if (reason != null)
            {
                _terminate(reason);
            }

            return reason;
        }
    }
}
