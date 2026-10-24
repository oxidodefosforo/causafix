using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace CausaFix.Revit
{
    /// <summary>
    /// Runs work in a valid Revit API context from a modeless window.
    ///
    /// Modeless windows cannot call the Revit API directly — being on the UI thread is not enough.
    /// Each request is queued and executed inside IExternalEventHandler.Execute, which Revit calls when
    /// it is ready. Callers await a Task that completes with a DTO (never Document/Element/Parameter).
    ///
    /// Long jobs are split by the caller into several requests (stages), so the UI stays responsive
    /// between stages and can cancel. Transactions never span requests.
    /// </summary>
    public sealed class RevitDispatcher : IExternalEventHandler, IDisposable
    {
        private interface IWorkItem
        {
            void Run(UIApplication app);
            void Cancel();
        }

        private sealed class WorkItem<T> : IWorkItem
        {
            private readonly Func<UIApplication, T> _func;
            private readonly CancellationToken _token;
            public readonly TaskCompletionSource<T> Tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            public WorkItem(Func<UIApplication, T> func, CancellationToken token) { _func = func; _token = token; }

            public void Run(UIApplication app)
            {
                if (_token.IsCancellationRequested) { Tcs.TrySetCanceled(); return; }
                try { Tcs.TrySetResult(_func(app)); }
                catch (OperationCanceledException) { Tcs.TrySetCanceled(); }
                catch (Exception ex) { Tcs.TrySetException(ex); }
            }

            public void Cancel() => Tcs.TrySetCanceled();
        }

        private readonly object _lock = new object();
        private readonly Queue<IWorkItem> _queue = new Queue<IWorkItem>();
        private readonly ExternalEvent _event;
        private bool _disposed;

        /// <summary>Must be created in a valid API context (e.g. inside IExternalCommand.Execute).</summary>
        public RevitDispatcher()
        {
            _event = ExternalEvent.Create(this);
        }

        public string GetName() => "CausaFix dispatcher";

        public int Pending { get { lock (_lock) return _queue.Count; } }

        public Task<T> InvokeAsync<T>(Func<UIApplication, T> func, CancellationToken token = default)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            var item = new WorkItem<T>(func, token);
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(RevitDispatcher));
                _queue.Enqueue(item);
            }
            // If Revit answers Denied/TimedOut the item simply stays queued and runs on the next accepted
            // raise (see Kick); nothing is lost or executed twice. "Pending" means an earlier raise will drain it.
            _event.Raise();
            return item.Tcs.Task;
        }

        public Task InvokeAsync(Action<UIApplication> action, CancellationToken token = default)
            => InvokeAsync<bool>(app => { action(app); return true; }, token);

        /// <summary>Called by Revit in API context. Drains the queue.</summary>
        public void Execute(UIApplication app)
        {
            while (true)
            {
                IWorkItem item;
                lock (_lock)
                {
                    if (_queue.Count == 0) return;
                    item = _queue.Dequeue();
                }
                item.Run(app);
            }
        }

        /// <summary>Re-raises the event if work is still queued (e.g. after a Denied raise).</summary>
        public void Kick()
        {
            if (Pending > 0) _event.Raise();
        }

        public void Dispose()
        {
            List<IWorkItem> leftover;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                leftover = new List<IWorkItem>(_queue);
                _queue.Clear();
            }
            foreach (var i in leftover) i.Cancel();
            _event.Dispose();
        }
    }
}
