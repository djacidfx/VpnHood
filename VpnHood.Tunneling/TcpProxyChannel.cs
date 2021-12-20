﻿using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling
{
    public class TcpProxyChannel : IChannel
    {
        private readonly object _lockCleanup = new();
        private readonly int _orgStreamReadBufferSize;
        private readonly TcpClientStream _orgTcpClientStream;
        private readonly int _tunnelStreamReadBufferSize;
        private readonly TcpClientStream _tunnelTcpClientStream;
        private readonly int BufferSize_Max = 0x14000 * 2;
        private bool _disposed;

        public static int c = 0; //todo

        public TcpProxyChannel(TcpClientStream orgTcpClientStream, TcpClientStream tunnelTcpClientStream,
            int orgStreamReadBufferSize = 0, int tunnelStreamReadBufferSize = 0)
        {
            _orgTcpClientStream = orgTcpClientStream ?? throw new ArgumentNullException(nameof(orgTcpClientStream));
            _tunnelTcpClientStream = tunnelTcpClientStream ?? throw new ArgumentNullException(nameof(tunnelTcpClientStream));
            if (orgStreamReadBufferSize == 0) orgStreamReadBufferSize = TunnelUtil.StreamBufferSize;
            if (tunnelStreamReadBufferSize == 0) tunnelStreamReadBufferSize = TunnelUtil.StreamBufferSize;

            _orgStreamReadBufferSize = orgStreamReadBufferSize > 0 && orgStreamReadBufferSize <= BufferSize_Max
                ? orgStreamReadBufferSize
                : throw new ArgumentOutOfRangeException($"Value must greater than 0 and less than {BufferSize_Max}", orgStreamReadBufferSize, nameof(orgStreamReadBufferSize));

            _tunnelStreamReadBufferSize = tunnelStreamReadBufferSize > 0 && tunnelStreamReadBufferSize <= BufferSize_Max
                ? tunnelStreamReadBufferSize
                : throw new ArgumentOutOfRangeException($"Value must greater than 0 and less than {BufferSize_Max}", tunnelStreamReadBufferSize, nameof(tunnelStreamReadBufferSize));

            Interlocked.Increment(ref c);
            VhLogger.Instance.LogWarning($"@TcpProxyChannel: {c}");
        }

        public event EventHandler<ChannelEventArgs>? OnFinished;
        public bool Connected { get; private set; }
        public long SentByteCount { get; private set; }
        public long ReceivedByteCount { get; private set; }
        public DateTime LastActivityTime { get; private set; } = DateTime.Now;

        public async Task Start()
        {
            Connected = true;
            try
            {
                var task1 = CopyToAsync(_tunnelTcpClientStream.Stream, _orgTcpClientStream.Stream, false, _tunnelStreamReadBufferSize, CancellationToken.None); // read
                var task2 = CopyToAsync(_orgTcpClientStream.Stream, _tunnelTcpClientStream.Stream, true, _orgStreamReadBufferSize, CancellationToken.None); //write
                await Task.WhenAll(task1, task2);
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            lock (_lockCleanup)
            {
                if (_disposed) return;
                _disposed = true;
            }

            Connected = false;
            _orgTcpClientStream.Dispose();
            _tunnelTcpClientStream.Dispose();
            
            Interlocked.Decrement(ref c);
            VhLogger.Instance.LogWarning($"@TcpProxyChannel: {c}");

            OnFinished?.Invoke(this, new ChannelEventArgs(this));
        }

        private async Task CopyToAsync(Stream source, Stream destination, bool isSendingOut, int bufferSize,
            CancellationToken cancellationToken)
        {
            try
            {
                await CopyToInternalAsync(source, destination, isSendingOut, bufferSize, cancellationToken);
            }
            catch (Exception ex)
            {
                // Dispose if any side throw an exception
                var message = isSendingOut ? "to" : "from";
                VhLogger.Instance.LogInformation(GeneralEventId.Tcp, $"TcpProxyChannel: Error in copying {message} tunnel. Message: {ex.Message}");
                Dispose();
            }
        }

        private async Task CopyToInternalAsync(Stream source, Stream destination, bool isSendingOut, int bufferSize,
            CancellationToken cancellationToken)
        {
            var doubleBuffer = false; //i am not sure it could help!

            // Microsoft Stream Source Code:
            // We pick a value that is the largest multiple of 4096 that is still smaller than the large object heap threshold (85K).
            // The CopyTo/CopyToAsync buffer is short-lived and is likely to be collected at Gen0, and it offers a significant
            // improvement in Copy performance.
            // 0x14000 recommended by microsoft for copying buffers
            if (bufferSize > BufferSize_Max)
                throw new ArgumentException($"Buffer is too big, maximum supported size is {BufferSize_Max}",
                    nameof(bufferSize));

            // <<----------------- the MOST memory consuming in the APP! >> ----------------------
            var readBuffer = new byte[bufferSize];
            var writeBuffer = doubleBuffer ? new byte[readBuffer.Length] : null;
            Task? writeTask = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                // read from source
                var bytesRead = await source.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken);
                if (writeTask != null)
                    await writeTask;

                // check end of the stream
                if (bytesRead == 0)
                    break;

                // write to destination
                if (writeBuffer != null)
                {
                    Array.Copy(readBuffer, writeBuffer, bytesRead);
                    writeTask = destination.WriteAsync(writeBuffer, 0, bytesRead, cancellationToken);
                }
                else
                {
                    await destination.WriteAsync(readBuffer, 0, bytesRead, cancellationToken);
                }

                // calculate transferred bytes
                if (!isSendingOut)
                    ReceivedByteCount += bytesRead;
                else
                    SentByteCount += bytesRead;

                // set LastActivityTime as some data delegated
                LastActivityTime = DateTime.Now;
            }
        }
    }
}