namespace Loupedeck.LightroomPresetsPlugin.Lightroom
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.IO;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    // A response from Lightroom's External Controller API, already detached
    // (via JsonElement.Clone()) from the JsonDocument it was parsed from, so
    // it stays valid after that document is disposed.
    public struct LightroomResponse
    {
        public Boolean Success;
        public Boolean HasResponse;
        public JsonElement Response;
    }

    // Owns a single, persistent WebSocket connection to Lightroom Desktop/CC's
    // local "External Controller API" and exposes a small request/response API
    // on top of it. Reconnection, pairing ("register") and liveness are all
    // handled here so the rest of the plugin never has to think about sockets.
    //
    // This class intentionally has zero dependency on the Loupedeck/Logi
    // PluginApi assembly - it only uses the .NET base class library - so it
    // can be compiled and unit-exercised on its own.
    public class LightroomConnection
    {
        private const String AppName = "Logi Lightroom Presets Plugin";
        private const String AppVersion = "1.0.0";
        private const String DefaultHost = "127.0.0.1";
        private const Int32 DefaultPort = 7682;
        private const String LightroomProcessPattern = "Adobe Lightroom";

        private const Int32 RequestTimeoutMs = 8000;
        private const Int32 RegisterTimeoutMs = 12000; // Longer: the user may need time to click "Pair" in Lightroom.
        private const Int32 PollIntervalMs = 3000;
        private const Double BackoffStartMs = 1000;
        private const Double BackoffMaxMs = 30000;

        private readonly Action<String, String> _log;
        private readonly ConcurrentDictionary<String, TaskCompletionSource<LightroomResponse>> _pending = new ConcurrentDictionary<String, TaskCompletionSource<LightroomResponse>>();

        private ClientWebSocket _socket;
        private CancellationTokenSource _loopCts;
        private Task _loopTask;
        private Task _receiveTask;

        private String _host = DefaultHost;
        private Int32 _port = DefaultPort;
        private String _clientGuid;
        private Boolean _connecting;
        private Boolean _stateLoaded;
        private DateTime _lastAttemptAt = DateTime.MinValue;
        private Double _backoffMs = BackoffStartMs;

        public LightroomConnectionStatus Status { get; private set; } = LightroomConnectionStatus.NotRunning;

        public event Action<LightroomConnectionStatus, LightroomConnectionStatus> StatusChanged;

        public LightroomConnection(Action<String, String> log = null)
        {
            this._log = log ?? ((level, message) => { });
        }

        public void ConfigureEndpoint(String host, Int32 port)
        {
            this._host = String.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();
            this._port = port > 0 ? port : DefaultPort;
        }

        // Begins the connect/poll loop. Safe to call once during plugin startup.
        public async Task StartAsync()
        {
            if (this._loopTask != null)
            {
                return;
            }

            await this.LoadStateAsync();
            await this.DetectPortAsync();

            this._loopCts = new CancellationTokenSource();
            this._loopTask = Task.Run(() => this.LoopAsync(this._loopCts.Token));
        }

        public async Task StopAsync()
        {
            if (this._loopCts != null)
            {
                this._loopCts.Cancel();
            }

            this.CloseSocket("plugin stopping");

            if (this._loopTask != null)
            {
                try { await this._loopTask; } catch { }
            }
        }

        // Sends a command to Lightroom and awaits the correlated response.
        // Throws if not currently connected - callers should check {@link Status}
        // first to give better user-facing errors.
        public async Task<LightroomResponse> SendRequestAsync(String message, Object[] parameters = null, Int32 timeoutMs = RequestTimeoutMs)
        {
            var socket = this._socket;
            if (socket == null || socket.State != WebSocketState.Open || this.Status != LightroomConnectionStatus.Connected)
            {
                throw new InvalidOperationException($"Cannot send \"{message}\": Lightroom is not connected (status: {this.Status})");
            }

            return await this.SendAndAwaitAsync(socket, message, parameters ?? Array.Empty<Object>(), timeoutMs);
        }

        private async Task<LightroomResponse> SendAndAwaitAsync(ClientWebSocket socket, String message, Object[] parameters, Int32 timeoutMs)
        {
            var requestId = Guid.NewGuid().ToString();
            var payload = JsonSerializer.Serialize(new
            {
                requestId = requestId,
                @object = (String)null,
                message = message,
                @params = parameters
            });

            var tcs = new TaskCompletionSource<LightroomResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            this._pending[requestId] = tcs;

            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                cts.Token.Register(() =>
                {
                    if (this._pending.TryRemove(requestId, out var pendingTcs))
                    {
                        pendingTcs.TrySetException(new TimeoutException($"Timed out waiting for Lightroom to respond to \"{message}\""));
                    }
                });

                var bytes = Encoding.UTF8.GetBytes(payload);
                await socket.SendAsync(new ArraySegment<Byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

                return await tcs.Task;
            }
        }

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await this.TickAsync();
                }
                catch (Exception ex)
                {
                    this._log("error", $"Connection poll loop failed: {ex.Message}");
                }

                try
                {
                    await Task.Delay(PollIntervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private async Task TickAsync()
        {
            var running = await this.IsLightroomRunningAsync();

            if (!running)
            {
                this.CloseSocket("Lightroom is not running");
                this.SetStatus(LightroomConnectionStatus.NotRunning);
                this._backoffMs = BackoffStartMs;
                return;
            }

            if (this.Status == LightroomConnectionStatus.Connected || this._connecting)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - this._lastAttemptAt).TotalMilliseconds < this._backoffMs)
            {
                return;
            }

            this._lastAttemptAt = now;
            await this.AttemptConnectAsync();
        }

        private async Task AttemptConnectAsync()
        {
            this._connecting = true;
            this.SetStatus(LightroomConnectionStatus.Connecting);

            var socket = new ClientWebSocket();

            try
            {
                using (var connectCts = new CancellationTokenSource(RequestTimeoutMs))
                {
                    await socket.ConnectAsync(new Uri($"ws://{this._host}:{this._port}"), connectCts.Token);
                }
            }
            catch (Exception ex)
            {
                this._log("debug", $"Lightroom socket connect failed: {ex.Message}");
                socket.Dispose();
                this._connecting = false;
                this.SetStatus(LightroomConnectionStatus.Disconnected);
                this.IncreaseBackoff();
                return;
            }

            this._socket = socket;
            this._receiveTask = Task.Run(() => this.ReceiveLoopAsync(socket));

            try
            {
                var response = await this.SendAndAwaitAsync(socket, "register", new Object[] { AppName, AppVersion, this._clientGuid }, RegisterTimeoutMs);
                this._connecting = false;

                if (response.Success)
                {
                    if (response.HasResponse && response.Response.ValueKind == JsonValueKind.Array && response.Response.GetArrayLength() > 0)
                    {
                        var first = response.Response[0];
                        if (first.ValueKind == JsonValueKind.String)
                        {
                            this._clientGuid = first.GetString();
                            await this.PersistStateAsync();
                        }
                    }

                    this.SetStatus(LightroomConnectionStatus.Connected);
                    this._backoffMs = BackoffStartMs;
                    this._log("info", "Registered with Lightroom's External Controller API");
                }
                else
                {
                    this.SetStatus(LightroomConnectionStatus.Unresponsive);
                    this.IncreaseBackoff();
                    this._log("warn", "Lightroom rejected the registration request");
                }
            }
            catch (Exception ex)
            {
                this._connecting = false;
                this.SetStatus(LightroomConnectionStatus.Unresponsive);
                this.IncreaseBackoff();
                this._log("warn", $"Lightroom did not answer the pairing handshake in time ({ex.Message}). If a \"Pair\" dialog is open in Lightroom, click Allow.");
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket)
        {
            var buffer = new Byte[8192];

            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    using (var stream = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<Byte>(buffer), CancellationToken.None);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                this.HandleClose("Lightroom closed the connection");
                                return;
                            }
                            stream.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        this.HandleMessage(Encoding.UTF8.GetString(stream.ToArray()));
                    }
                }
            }
            catch (Exception ex)
            {
                this._log("debug", $"Lightroom receive loop ended: {ex.Message}");
                this.HandleClose(ex.Message);
            }
        }

        private void HandleMessage(String json)
        {
            String requestId = null;
            LightroomResponse response = default;

            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    if (root.TryGetProperty("requestId", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                    {
                        requestId = idElement.GetString();
                    }

                    response.Success = root.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.True;

                    if (root.TryGetProperty("response", out var responseElement))
                    {
                        response.HasResponse = true;
                        response.Response = responseElement.Clone();
                    }
                }
            }
            catch (JsonException)
            {
                this._log("warn", $"Received non-JSON message from Lightroom: {Truncate(json)}");
                return;
            }

            if (requestId != null && this._pending.TryRemove(requestId, out var tcs))
            {
                tcs.TrySetResult(response);
                return;
            }

            this._log("debug", $"Unsolicited message from Lightroom: {Truncate(json)}");
        }

        private void HandleClose(String reason)
        {
            foreach (var kvp in this._pending)
            {
                if (this._pending.TryRemove(kvp.Key, out var tcs))
                {
                    tcs.TrySetException(new IOException(reason));
                }
            }

            this._connecting = false;

            if (this.Status != LightroomConnectionStatus.NotRunning)
            {
                this.SetStatus(LightroomConnectionStatus.Disconnected);
                this.IncreaseBackoff();
            }
        }

        private void CloseSocket(String reason)
        {
            var socket = this._socket;
            this._socket = null;

            if (socket != null)
            {
                this._log("debug", $"Closing Lightroom socket: {reason}");
                try
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        socket.Abort();
                    }
                }
                catch { }
                socket.Dispose();
            }

            foreach (var kvp in this._pending)
            {
                if (this._pending.TryRemove(kvp.Key, out var tcs))
                {
                    tcs.TrySetException(new IOException(reason));
                }
            }
        }

        private void IncreaseBackoff() => this._backoffMs = Math.Min(this._backoffMs * 2, BackoffMaxMs);

        private void SetStatus(LightroomConnectionStatus next)
        {
            var previous = this.Status;
            if (previous == next)
            {
                return;
            }

            this.Status = next;
            this._log("info", $"Lightroom connection status: {previous} -> {next}");
            this.StatusChanged?.Invoke(next, previous);
        }

        // macOS-only, read-only process check (fixed argv via ProcessStartInfo.ArgumentList,
        // never a shell string - not susceptible to injection). Used purely to distinguish
        // "Lightroom isn't open" from "Lightroom is open but unreachable" for clearer error
        // messages; never used to control or automate Lightroom.
        private async Task<Boolean> IsLightroomRunningAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "pgrep",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add(LightroomProcessPattern);

                using (var process = Process.Start(startInfo))
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                this._log("debug", $"Could not check whether Lightroom is running: {ex.Message}");
                return false;
            }
        }

        private async Task LoadStateAsync()
        {
            if (this._stateLoaded)
            {
                return;
            }
            this._stateLoaded = true;

            try
            {
                var path = PluginPaths.ConnectionStatePath;
                if (File.Exists(path))
                {
                    var json = await File.ReadAllTextAsync(path);
                    using (var doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("clientGuid", out var guidElement) && guidElement.ValueKind == JsonValueKind.String)
                        {
                            this._clientGuid = guidElement.GetString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this._log("debug", $"No prior Lightroom pairing state loaded: {ex.Message}");
            }
        }

        private async Task PersistStateAsync()
        {
            try
            {
                Directory.CreateDirectory(PluginPaths.AppSupportDirectory);
                var json = JsonSerializer.Serialize(new { clientGuid = this._clientGuid }, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(PluginPaths.ConnectionStatePath, json);
            }
            catch (Exception ex)
            {
                this._log("warn", $"Failed to persist Lightroom pairing state: {ex.Message}");
            }
        }

        // Best-effort scan of Lightroom's own connections folder for a
        // non-default port. Adobe does not document this file's schema, so
        // any failure here silently keeps the default 127.0.0.1:7682.
        private async Task DetectPortAsync()
        {
            try
            {
                var dir = PluginPaths.LightroomConnectionsDirectory;
                if (!Directory.Exists(dir))
                {
                    return;
                }

                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    var json = await File.ReadAllTextAsync(file);
                    using (var doc = JsonDocument.Parse(json))
                    {
                        foreach (var key in new[] { "port", "Port", "websocketPort", "webSocketPort" })
                        {
                            if (doc.RootElement.TryGetProperty(key, out var portElement) && portElement.ValueKind == JsonValueKind.Number)
                            {
                                this._port = portElement.GetInt32();
                                this._log("info", $"Discovered External Controller API port {this._port} from Lightroom's connections folder");
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this._log("debug", $"Falling back to default Lightroom port: {ex.Message}");
            }
        }

        private static String Truncate(String text, Int32 max = 300) => text.Length > max ? text.Substring(0, max) + "…" : text;
    }
}
