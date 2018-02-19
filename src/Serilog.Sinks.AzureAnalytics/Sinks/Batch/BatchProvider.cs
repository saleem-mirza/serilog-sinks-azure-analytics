// Copyright 2018 Zethian Inc.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Serilog.Sinks.Batch
{
    internal abstract class BatchProvider : IDisposable
    {
        private const    int  MaxSupportedBufferSize = 100_000;
        private const    int  MaxSupportedBatchSize  = 1_000;
        private          int  _numMessages;
        private          bool _canStop;
        private readonly int  _maxBufferSize;
        private readonly int  _batchSize;
        private readonly CancellationTokenSource _cancellationToken = new CancellationTokenSource();
        private readonly CancellationTokenSource _eventCancellationToken = new CancellationTokenSource();
        private readonly ConcurrentQueue<LogEvent> _logEventBatch;
        private readonly BlockingCollection<IList<LogEvent>> _batchEventsCollection;
        private readonly BlockingCollection<LogEvent> _eventsCollection;
        private readonly TimeSpan       _thresholdTimeSpan = TimeSpan.FromSeconds(10);
        private readonly AutoResetEvent _timerResetEvent   = new AutoResetEvent(false);
        private readonly Task           _timerTask;
        private readonly Task           _batchTask;
        private readonly Task           _eventPumpTask;
        private readonly List<Task>     _workerTasks = new List<Task>();
        private static   SemaphoreSlim  _semaphore;

        protected BatchProvider(int batchSize = 100, int maxBufferSize = 25_000)
        {
            _semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
            _maxBufferSize = Math.Min(Math.Max(5_000, maxBufferSize), MaxSupportedBufferSize);
            _batchSize     = Math.Min(Math.Max(batchSize, 1), MaxSupportedBatchSize);

            _logEventBatch         = new ConcurrentQueue<LogEvent>();
            _batchEventsCollection = new BlockingCollection<IList<LogEvent>>();
            _eventsCollection      = new BlockingCollection<LogEvent>(maxBufferSize);

            _batchTask     = Task.Factory.StartNew(Pump, TaskCreationOptions.LongRunning);
            _timerTask     = Task.Factory.StartNew(TimerPump, TaskCreationOptions.LongRunning);
            _eventPumpTask = Task.Factory.StartNew(EventPump, TaskCreationOptions.LongRunning);
        }

        private void Pump()
        {
            try
            {
                while (true)
                {
                    _semaphore.Wait();

                    var logEvents = _batchEventsCollection.Take(_cancellationToken.Token);
                    _workerTasks.Add(
                        Task.Factory.StartNew(
                            async (workerTask) =>
                            {
                                try
                                {
                                    var messageList = workerTask as IList<LogEvent>;
                                    if (messageList == null || messageList.Count == 0)
                                    {
                                        return;
                                    }

                                    var retValue = await WriteLogEventAsync(messageList)
                                        .ConfigureAwait(false);
                                    if (retValue)
                                    {
                                        Interlocked.Add(ref _numMessages, -1 * messageList.Count);
                                    }
                                    else
                                    {
                                        SelfLog.WriteLine("Retrying after 10 seconds...");

                                        await Task.Delay(TimeSpan.FromSeconds(10))
                                            .ConfigureAwait(false);

                                        _batchEventsCollection.Add(messageList);
                                    }
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            },
                            logEvents));

                    Task.WhenAny(_workerTasks)
                        .ContinueWith(a => { _workerTasks.RemoveAll(t => t.IsCompleted); });
                }
            }
            catch (OperationCanceledException)
            {
                SelfLog.WriteLine("Shutting down batch processing");
            }
            catch (Exception e)
            {
                SelfLog.WriteLine(e.Message);
            }
        }

        private void TimerPump()
        {
            while (!_canStop)
            {
                _timerResetEvent.WaitOne(_thresholdTimeSpan);
                FlushLogEventBatch();
            }
        }

        private void EventPump()
        {
            try
            {
                while (true)
                {
                    var logEvent = _eventsCollection.Take(_eventCancellationToken.Token);
                    _logEventBatch.Enqueue(logEvent);

                    if (_logEventBatch.Count >= _batchSize)
                    {
                        FlushLogEventBatch();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                SelfLog.WriteLine("Shutting down event pump");
            }
            catch (Exception e)
            {
                SelfLog.WriteLine(e.Message);
            }
        }

        private void FlushLogEventBatch()
        {
            if (!_logEventBatch.Any())
            {
                return;
            }

            var logEventBatchSize = _logEventBatch.Count >= _batchSize ? (int)_batchSize : _logEventBatch.Count;
            var logEventList = new List<LogEvent>();

            for (var i = 0; i < logEventBatchSize; i++)
            {
                if (_logEventBatch.TryDequeue(out LogEvent logEvent))
                {
                    logEventList.Add(logEvent);
                }
            }
            _batchEventsCollection.Add(logEventList);
        }

        protected void PushEvent(LogEvent logEvent)
        {
            if (_numMessages > _maxBufferSize)
                return;
            _eventsCollection.Add(logEvent);
            Interlocked.Increment(ref _numMessages);
        }

        protected abstract Task<bool> WriteLogEventAsync(ICollection<LogEvent> logEventsBatch);

        #region IDisposable Support

        private bool _disposedValue; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    FlushAndCloseEventHandlers();

                    SelfLog.WriteLine("Sink halted successfully.");
                }

                _disposedValue = true;
            }
        }

        private void FlushAndCloseEventHandlers()
        {
            try
            {
                SelfLog.WriteLine("Halting sink...");

                _eventCancellationToken.Cancel();
                _cancellationToken.Cancel();

                Task.WaitAll(_workerTasks.ToArray());

                _canStop = true;
                _timerResetEvent.Set();

                // Flush events collection
                while (_eventsCollection.TryTake(out LogEvent logEvent))
                {
                    _logEventBatch.Enqueue(logEvent);

                    if (_logEventBatch.Count >= _batchSize)
                    {
                        FlushLogEventBatch();
                    }
                }

                FlushLogEventBatch();

                // Flush events batch
                while (_batchEventsCollection.TryTake(out IList<LogEvent> eventBatch))
                    WriteLogEventAsync(eventBatch)
                        .Wait(TimeSpan.FromSeconds(30));

                Task.WaitAll(
                    new[]
                    {
                        _eventPumpTask,
                        _batchTask,
                        _timerTask
                    },
                    TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine(ex.Message);
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}