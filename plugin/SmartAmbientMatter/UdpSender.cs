using System;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using SmartAmbientMatter.Models;

namespace SmartAmbientMatter
{
    /// <summary>
    /// Sends zone-aware lighting commands as JSON UDP packets to the Python Matter bridge.
    /// Fire-and-forget — no acknowledgment is expected or awaited.
    /// A single UdpClient is reused across sends for efficiency.
    /// </summary>
    public class UdpSender : IDisposable
    {
        private UdpClient _client;
        private bool _disposed;

        public UdpSender()
        {
            _client = new UdpClient();
        }

        /// <summary>
        /// Serializes a LightingState and sends it as a UDP packet to the bridge.
        /// </summary>
        /// <param name="host">Bridge host (e.g. "127.0.0.1").</param>
        /// <param name="port">Bridge UDP port (e.g. 10001).</param>
        /// <param name="zoneName">Zone name, used by bridge to route to correct bulbs.</param>
        /// <param name="state">Lighting state (Kelvin, Brightness, Transition already applied).</param>
        public void Send(string host, int port, string zoneName, LightingState state)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UdpSender));

            var payload = new
            {
                zone       = zoneName,
                kelvin     = state.Kelvin,
                brightness = state.Brightness,
                transition = state.Transition
            };

            string json = JsonConvert.SerializeObject(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            _client.Send(bytes, bytes.Length, host, port);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _client?.Close();
                _client = null;
                _disposed = true;
            }
        }
    }
}
