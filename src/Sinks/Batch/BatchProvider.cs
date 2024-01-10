// Copyright 2025 Zethian Inc.
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
        private const int MaxSupportedBufferSize = 100_000;
        private const int MaxSupportedBatchSize = 1_000;
        private int _numMessages;
        private readonly int _maxBufferSize;
        private readonly int _batchSize;
        private readonly BlockingCollection<IList<LogEvent>> _batchEventsCollection;
        private readonly BlockingCollection<LogEvent> _eventsCollection;
        private readonly TimeSpan _timerThresholdSpan;
        private readonly TimeSpan _transientThresholdSpan;
        private readonly Task _timerTask;
        private readonly Task _batchTask;
        private readonly AutoResetEvent _timerResetEvent;
        private readonly SemaphoreSlim _semaphoreSlim;
        private readonly CountdownEvent _cde = new CountdownEvent(2);
        private CancellationTokenSource _cancelationTokenSrc = new CancellationTokenSource();


        protected BatchProvider(int batchSize = 100, int maxBufferSize = 25_000)
        {
            _maxBufferSize = Math.Min(Math.Max(1, maxBufferSize), MaxSupportedBufferSize);
            _batchSize = Math.Min(Math.Max(batchSize, 1), MaxSupportedBatchSize);

            _batchEventsCollection = new BlockingCollection<IList<LogEvent>>(maxBufferSize);
            _eventsCollection = new BlockingCollection<LogEvent>(maxBufferSize);

            _timerThresholdSpan = TimeSpan.FromSeconds(15);
            _transientThresholdSpan = TimeSpan.FromSeconds(5);

            _timerResetEvent = new AutoResetEvent(false);
            _semaphoreSlim = new SemaphoreSlim(1, 1);

            _batchTask = Task.Factory.StartNew(BatchTask, TaskCreationOptions.LongRunning);
            _timerTask = Task.Factory.StartNew(TimerPump, TaskCreationOptions.LongRunning);
        }

        private async Task BatchTask()
        {
            try
            {
                while (!_cancelationTokenSrc.IsCancellationRequested)
                {
                    var logEvents = _batchEventsCollection.Take(_cancelationTokenSrc.Token);
                    var currentEventbatchSize = logEvents.Count;

                    SelfLog.WriteLine($"Sending batch of {currentEventbatchSize} logs");

                    var retValue = await WriteLogEventAsync(logEvents).ConfigureAwait(false);
                    if (!retValue)
                    {
                        SelfLog.WriteLine($"Retrying after {_transientThresholdSpan.TotalSeconds} seconds...");

                        await Task.Delay(_transientThresholdSpan).ConfigureAwait(false);

                        _batchEventsCollection.Add(logEvents);
                    }
                    Interlocked.Add(ref _numMessages, - currentEventbatchSize);
                }
            }
            catch (InvalidOperationException) { }
            catch (OperationCanceledException ox)
            {
                SelfLog.WriteLine(ox.Message);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine(ex.Message);
            }
            finally
            {
                _cde.Signal();
            }
        }

        private void TimerPump()
        {
            try
            {
                while (true)
                {
                    if(_timerResetEvent.WaitOne(_timerThresholdSpan, true) == true)
                    {
                        break;
                    }

                    if (_eventsCollection.Count > 0)
                    {
                        var eventList = new List<LogEvent>();
                        for (var i = 0; i < Math.Min(_batchSize, _eventsCollection.Count); i++)
                        {
                            eventList.Add(_eventsCollection.Take());

                        }
                        if (!_batchEventsCollection.IsAddingCompleted)
                        {
                            _batchEventsCollection.Add(eventList);
                        }
                    }
                }

            }
            finally
            {
                _cde.Signal();
            }
        }

        protected void PushEvent(LogEvent logEvent)
        {
            if (_numMessages > _maxBufferSize)
            {
                SelfLog.WriteLine("<bufferSize> value is too low, discarding message");
                return;
            }

            if (_eventsCollection.IsAddingCompleted)
                return;

            if (_eventsCollection.Count >= _batchSize)
            {
                var eventList = new List<LogEvent>();
                for (var i = 0; i < _batchSize; i++)
                {
                    eventList.Add(_eventsCollection.Take());
                }
                if (!_batchEventsCollection.IsAddingCompleted)
                {
                    _batchEventsCollection.Add(eventList);
                }
            }
            _eventsCollection.Add(logEvent);
            Interlocked.Increment(ref _numMessages);
        }

        protected abstract Task<bool> WriteLogEventAsync(ICollection<LogEvent> logEventsBatch);

        #region IDisposable Support

        private bool _disposedValue; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue)
                return;

            CloseAndFlushEvents();

            if (disposing)
            {
                _semaphoreSlim.Dispose();

                SelfLog.WriteLine("Sink halted successfully.");
            }

            _disposedValue = true;
        }

        private void CloseAndFlushEvents()
        {
            try
            {
                SelfLog.WriteLine("Halting sink...");
                _eventsCollection.CompleteAdding();
                _cancelationTokenSrc.Cancel();

                _timerResetEvent.Set();
                _cde.Wait();

                Task.WaitAll(new[] { _batchTask, _timerTask }, TimeSpan.FromSeconds(60));
                if (!_batchEventsCollection.IsCompleted)
                {
                    foreach (var logEvent in _batchEventsCollection)
                    {
                        SelfLog.WriteLine($"Sending batch of {logEvent.Count} logs");
                        WriteLogEventAsync(logEvent).GetAwaiter().GetResult();
                    }
                }

                if (!_eventsCollection.IsCompleted)
                {
                    SelfLog.WriteLine($"Sending batch of {_eventsCollection.Count} logs");
                    WriteLogEventAsync(_eventsCollection.ToList()).GetAwaiter().GetResult();
                }

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
