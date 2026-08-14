// Copyright 2019-2026 Zethian Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Serilog.Debugging;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Serilog.Sinks.Batch
{
    internal abstract class BatchProvider : IDisposable
    {
        private const int MaxSupportedBufferSize = 100_000;
        private const int MaxSupportedBatchSize  = 1_000;
        private const int MaxBatchRetries        = 5;
        private const double MaxRetryDelaySeconds = 60.0;

        private static readonly TimeSpan FlushInterval       = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ShutdownGrace       = TimeSpan.FromSeconds(60);

        private readonly int _batchSize;
        private readonly Channel<LogEvent> _channel;
        private readonly ChannelWriter<LogEvent> _writer;
        private readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();
        private readonly Task _writerTask;

        private long _droppedCount;
        private int  _disposed;

        protected BatchProvider(int batchSize = 100, int maxBufferSize = 25_000)
        {
            _batchSize = Math.Min(Math.Max(batchSize, 1), MaxSupportedBatchSize);

            // Buffer must hold at least one batch; above that, fully user-controlled
            // so memory-constrained hosts can cap channel memory at small values.
            var bufferSize = Math.Min(Math.Max(maxBufferSize, _batchSize), MaxSupportedBufferSize);

            // DropWrite + TryWrite returns false immediately when full, which we
            // surface as a counted drop. DropNewest/DropOldest always return true
            // from TryWrite (silently discarding items), hiding the drop. Wait has
            // identical TryWrite semantics to DropWrite but implies callers may
            // block via WriteAsync — misleading since we only ever call TryWrite.
            _channel = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(bufferSize) {
                FullMode                      = BoundedChannelFullMode.DropWrite,
                SingleReader                  = true,
                SingleWriter                  = false,
                AllowSynchronousContinuations = false,
            });
            _writer = _channel.Writer;

            _writerTask = Task.Run(RunAsync);
        }

        protected void PushEvent(LogEvent logEvent)
        {
            if (_writer.TryWrite(logEvent)) return;

            var dropped = Interlocked.Increment(ref _droppedCount);
            if (dropped == 1 || dropped % 1_000 == 0) {
                SelfLog.WriteLine("Buffer full; {0} events dropped so far.", dropped);
            }
        }

        protected abstract Task<bool> WriteLogEventAsync(ICollection<LogEvent> logEventsBatch);

        private async Task RunAsync()
        {
            var reader = _channel.Reader;
            var batch  = new List<LogEvent>(_batchSize);

            try {
                // WaitToReadAsync returns false once the channel is completed AND drained,
                // which is the natural exit path on Dispose.
                while (await reader.WaitToReadAsync().ConfigureAwait(false)) {
                    batch.Clear();
                    DrainAvailable(reader, batch);

                    if (batch.Count > 0 && batch.Count < _batchSize) {
                        await FillBatchWithDeadlineAsync(reader, batch).ConfigureAwait(false);
                    }

                    if (batch.Count > 0) {
                        await WriteWithRetryAsync(batch).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) {
                // The loop body itself should be exception-safe (WriteWithRetryAsync swallows
                // all subclass exceptions). Anything bubbling here is a defect; log and exit.
                SelfLog.WriteLine("Writer loop exited unexpectedly: {0}", ex);
            }
        }

        private void DrainAvailable(ChannelReader<LogEvent> reader, List<LogEvent> batch)
        {
            while (batch.Count < _batchSize && reader.TryRead(out var ev)) {
                batch.Add(ev);
            }
        }

        private async Task FillBatchWithDeadlineAsync(ChannelReader<LogEvent> reader, List<LogEvent> batch)
        {
            // Wait up to FlushInterval for the partial batch to fill. Shutdown also
            // breaks us out early via _shutdownCts so we flush what we have promptly.
            using (var deadlineCts = new CancellationTokenSource(FlushInterval))
            using (var linkedCts   = CancellationTokenSource.CreateLinkedTokenSource(deadlineCts.Token, _shutdownCts.Token))
            {
                try {
                    while (batch.Count < _batchSize &&
                           await reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
                    {
                        DrainAvailable(reader, batch);
                    }
                }
                catch (OperationCanceledException) {
                    // Deadline or shutdown fired — flush what we have.
                }
            }
        }

        private async Task WriteWithRetryAsync(List<LogEvent> batch)
        {
            for (var attempt = 0; attempt <= MaxBatchRetries; attempt++) {
                bool success = false;
                try {
                    success = await WriteLogEventAsync(batch).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    SelfLog.WriteLine("WriteLogEventAsync threw on attempt {0}: {1}", attempt + 1, ex);
                }

                if (success) return;

                if (attempt == MaxBatchRetries) {
                    SelfLog.WriteLine("Dropping batch of {0} events after {1} retries failed.",
                        batch.Count, MaxBatchRetries);
                    return;
                }

                var delaySecs = Math.Min(TransientRetryDelay.TotalSeconds * Math.Pow(2, attempt), MaxRetryDelaySeconds);
                SelfLog.WriteLine("Retrying batch in {0}s (attempt {1}/{2})...",
                    delaySecs, attempt + 1, MaxBatchRetries);

                try {
                    await Task.Delay(TimeSpan.FromSeconds(delaySecs), _shutdownCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    SelfLog.WriteLine("Shutdown during retry backoff; dropping batch of {0} events.", batch.Count);
                    return;
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            try {
                SelfLog.WriteLine("Halting sink...");

                // 1. Stop accepting new events — outstanding ones stay in the channel.
                _writer.TryComplete();

                // 2. Cut short any retry backoff and partial-batch wait so the writer
                //    drains as fast as possible. The drain itself still happens because
                //    WaitToReadAsync in the outer loop is NOT bound to _shutdownCts —
                //    it exits only when the channel is completed AND empty.
                _shutdownCts.Cancel();

                if (!_writerTask.Wait(ShutdownGrace)) {
                    SelfLog.WriteLine("Writer did not drain within {0}s; abandoning.", ShutdownGrace.TotalSeconds);
                }
            }
            catch (Exception ex) {
                SelfLog.WriteLine("Error during dispose: {0}", ex);
            }
            finally {
                _shutdownCts.Dispose();
                SelfLog.WriteLine("Sink halted successfully.");
            }
        }

    }
}
