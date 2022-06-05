using System;
using System.Diagnostics;
using System.Threading;

namespace richter_waitlock1
{
    class Program
    {
        static void Main(string[] args)
        {
            var x = 0;
            const int iterations = 5000000;

            Stopwatch sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                x++;
            }
            Console.WriteLine("Inc x: {0:N0}", sw.ElapsedMilliseconds);
            
            sw.Restart();
            for (var i = 0; i < iterations; i++)
            {
                Check();
                x++;
                Check();
            }
            Console.WriteLine("Inc x with Nothing: {0:N0}", sw.ElapsedMilliseconds);

            sw.Restart();
            var spinLock = new SpinLock();
            for (var i = 0; i < iterations; i++)
            {
                var taken = false;
                spinLock.Enter(ref taken);
                x++;
                spinLock.Exit();
            }
            Console.WriteLine("Inc x with stock SpinLock: {0:N0}", sw.ElapsedMilliseconds);

            sw.Restart();
            var interlockedLock = new InterlockedLock();
            for (var i = 0; i < iterations; i++)
            {
                interlockedLock.Enter();
                x++;
                interlockedLock.Exit();
            }
            Console.WriteLine("Inc x with InterlockedLock: {0:N0}", sw.ElapsedMilliseconds);

            sw.Restart();
            using(var eventLock = new EventLock())
            {
                for (var i = 0; i < iterations; i++)
                {
                    eventLock.Enter();
                    x++;
                    eventLock.Exit();
                }
            }
            Console.WriteLine("Inc x with EventLock: {0:N0}", sw.ElapsedMilliseconds);

            sw.Restart();
            using (var hybridLock = new HybridLock())
            {
                for (var i = 0; i < iterations; i++)
                {
                    hybridLock.Enter();
                    x++;
                    hybridLock.Exit();
                }
            }
            Console.WriteLine("Inc x with HybridLock: {0:N0}", sw.ElapsedMilliseconds);

            sw.Restart();
            using (var timedHybridLock = new TimedHybridLock())
            {
                for (var i = 0; i < iterations; i++)
                {
                    timedHybridLock.Enter();
                    x++;
                    timedHybridLock.Exit();
                }
            }
            Console.WriteLine("Inc x with TimedHybridLock: {0:N0}", sw.ElapsedMilliseconds);

        }
        
        private static void Check() { }
    }

    class InterlockedLock
    {
        private int _inUse;

        public void Enter()
        {
            while (true)
            {
                if (Interlocked.Exchange(ref _inUse, 1) == 0)
                    return;
            }
        }

        public void Exit()
        {
            Interlocked.Exchange(ref _inUse, 0);
        }
    }

    class EventLock : IDisposable
    {
        private readonly AutoResetEvent _available;

        public EventLock()
        {
            _available = new AutoResetEvent(true);
        }

        public void Enter()
        {
            _available.WaitOne();
        }

        public void Exit()
        {
            _available.Set();
        }

        public void Dispose()
        {
            _available.Dispose();
        }
    }

    class HybridLock : IDisposable
    {
        private int _waitingThreads = 0;
        private readonly AutoResetEvent _autoResetEvent = new AutoResetEvent(false);

        public void Enter()
        {
            if (Interlocked.Increment(ref _waitingThreads) == 1)
                return;
            _autoResetEvent.WaitOne();
        }

        public void Exit()
        {
            if (Interlocked.Decrement(ref _waitingThreads) == 0)
                return;
            _autoResetEvent.Set();
        }

        public void Dispose()
        {
            _autoResetEvent.Dispose();
        }
    }

    class TimedHybridLock : IDisposable
    {
        private int _waitingThreads = 0;
        private AutoResetEvent _autoResetEvent = new AutoResetEvent(false);
        private int _spins = 4000;
        private int _owningThreadId = 0;
        private int _recursion = 0;

        public void Enter()
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;
            if (threadId == _owningThreadId)
            {
                _recursion++;
                return;
            }

            var spinWait = new SpinWait();
            for (var i = 0; i < _spins; i++)
            {
                if (Interlocked.CompareExchange(ref _waitingThreads, 1, 0) == 0)
                {
                    SetOwnership(threadId);
                    return;
                }

                spinWait.SpinOnce();
            }

            if (Interlocked.Increment(ref _waitingThreads) > 1)
            {
                _autoResetEvent.WaitOne();
            }
        }

        public void Exit()
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;
            if (threadId != _owningThreadId)
            {
                throw new SynchronizationLockException("owning thread id error");
            }
            
            if (--_recursion > 0)
                return;

            _owningThreadId = 0;

            if (Interlocked.Decrement(ref _waitingThreads) == 0)
                return;

            _autoResetEvent.Set();
        }

        public void Dispose()
        {
            _autoResetEvent.Dispose();
        }

        private void SetOwnership(int threadId)
        {
            _owningThreadId = threadId;
            _recursion = 1;
        }
    }
}
