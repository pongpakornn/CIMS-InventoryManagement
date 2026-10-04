using System;
using System.Windows.Input;

namespace CIMS
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        // 1. สำหรับรับ Parameter แบบ object (ของเดิม)
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // 2. เพิ่ม: สำหรับรับ Method ที่ไม่มี Parameter เช่น SaveCommand = new RelayCommand(SaveAction);
        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(p => execute(), p => canExecute?.Invoke() ?? true)
        {
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    // 3. เพิ่ม: Generic Class เพื่อให้ใช้ RelayCommand<Product> ได้
    public class RelayCommand<T> : RelayCommand
    {
        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
            : base(p => execute((T)p), p => canExecute == null || canExecute((T)p))
        {
        }
    }

}