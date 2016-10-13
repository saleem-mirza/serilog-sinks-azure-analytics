﻿// Copyright 2016 Zethian Inc.
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Debugging;
using Serilog.Events;

namespace Serilog.Sinks.AzureLogAnalytics.Batch
{
    internal abstract class BatchProvider : IDisposable
    {
        private readonly uint _batchSize;
        private readonly CancellationTokenSource _cancellationToken = new CancellationTokenSource();
        private readonly List<LogEvent> _logEventBatch;
        private readonly BlockingCollection<IList<LogEvent>> _messageQueue;
        private readonly TimeSpan _thresholdTimeSpan = TimeSpan.FromSeconds(10);
        private readonly AutoResetEvent _timerResetEvent = new AutoResetEvent(false);
        private readonly Task _timerTask;
        private readonly List<Thread> _workerThreads;

        private bool _canStop;

        protected BatchProvider(uint batchSize = 100) : this(batchSize, 1)
        {
        }

        protected BatchProvider(uint batchSize, int nThreads = 1)
        {
            _batchSize = batchSize;
            _logEventBatch = new List<LogEvent>();
            _messageQueue = new BlockingCollection<IList<LogEvent>>();
            _workerThreads = new List<Thread>();

            var maxThreads = Math.Max(1, Math.Min(nThreads, Environment.ProcessorCount));
            for (var i = 0; i < maxThreads; i++)
            {
                var workerThread = new Thread(Pump)
                {
                    IsBackground = true
                };
                workerThread.Start();
                _workerThreads.Add(workerThread);
            }

            _timerTask = Task.Factory.StartNew(TimerPump);
        }

        private void Pump()
        {
            try
            {
                while (true)
                {
                    var logEvents = _messageQueue.Take(_cancellationToken.Token);
                    WriteLogEvent(logEvents);
                }
            }
            catch (OperationCanceledException)
            {
                _canStop = true;
                _timerResetEvent.Set();
                _timerTask.Wait();

                IList<LogEvent> eventBatch;
                while (_messageQueue.TryTake(out eventBatch))
                    WriteLogEvent(eventBatch);
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

        private void FlushLogEventBatch()
        {
            lock (this)
            {
                _messageQueue.Add(_logEventBatch.ToArray());
                _logEventBatch.Clear();
            }
        }

        protected void PushEvent(LogEvent logEvent)
        {
            _logEventBatch.Add(logEvent);
            if (_logEventBatch.Count >= _batchSize)
                FlushLogEventBatch();
        }

        protected abstract void WriteLogEvent(ICollection<LogEvent> logEventsBatch);

        #region IDisposable Support

        private bool _disposedValue; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    SelfLog.WriteLine("Halting sink...");
                    _cancellationToken.Cancel();
                    foreach (var workerThread in _workerThreads)
                        workerThread.Join();
                    SelfLog.WriteLine("Sink halted successfully.");
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}