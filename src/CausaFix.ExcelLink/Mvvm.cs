using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CausaFix.ExcelLink
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _run;
        private readonly Func<object, bool> _can;

        public RelayCommand(Action run, Func<bool> can = null) : this(_ => run(), can == null ? (Func<object, bool>)null : _ => can()) { }
        public RelayCommand(Action<object> run, Func<object, bool> can = null) { _run = run; _can = can; }

        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => _can == null || _can(parameter);
        public void Execute(object parameter) { if (CanExecute(parameter)) _run(parameter); }
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Async command; never runs twice concurrently; errors go to the supplied handler.</summary>
    public sealed class AsyncCommand : ICommand
    {
        private readonly Func<object, Task> _run;
        private readonly Func<object, bool> _can;
        private readonly Action<Exception> _onError;
        private bool _running;

        public AsyncCommand(Func<Task> run, Func<bool> can, Action<Exception> onError)
            : this(_ => run(), can == null ? (Func<object, bool>)null : _ => can(), onError) { }

        public AsyncCommand(Func<object, Task> run, Func<object, bool> can, Action<Exception> onError)
        {
            _run = run; _can = can; _onError = onError;
        }

        public event EventHandler CanExecuteChanged;
        public bool IsRunning => _running;
        public bool CanExecute(object parameter) => !_running && (_can == null || _can(parameter));

        public async void Execute(object parameter) => await ExecuteAsync(parameter);

        public async Task ExecuteAsync(object parameter = null)
        {
            if (!CanExecute(parameter)) return;
            _running = true;
            Refresh();
            try { await _run(parameter); }
            catch (Exception ex) { _onError?.Invoke(ex); }
            finally
            {
                _running = false;
                Refresh();
            }
        }

        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A checkable / selectable list item.</summary>
    public class Selectable<T> : ObservableObject
    {
        private bool _isChecked;
        private bool _isSelected;
        private bool _isVisible = true;

        public Selectable(T item) { Item = item; }

        public T Item { get; }
        public bool IsChecked { get => _isChecked; set { if (Set(ref _isChecked, value)) CheckedChanged?.Invoke(this, EventArgs.Empty); } }
        /// <summary>Highlighted in the list (multi-selection for add/remove).</summary>
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
        public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

        public event EventHandler CheckedChanged;

        /// <summary>Sets the flag without raising CheckedChanged (bulk operations raise once at the end).</summary>
        public void SetCheckedSilently(bool value)
        {
            _isChecked = value;
            OnPropertyChanged(nameof(IsChecked));
        }
    }
}
