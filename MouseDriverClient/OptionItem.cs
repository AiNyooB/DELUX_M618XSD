using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MouseDriverClient
{
    /// <summary>
    /// 类型安全的下拉选项：文本 + 业务值分离，彻底替代
    /// <c>ComboBoxItem</c> + <c>Tag</c> 的脆弱映射（old：Tag 是 string 还是 int
    /// 不确定导致逻辑静默失效）。XAML 用 DisplayMemberPath="Text" /
    /// <c>SelectedValuePath="Value"</c> 绑定，业务值始终为 T。
    /// </summary>
    public class OptionItem<T> : INotifyPropertyChanged
    {
        private string _text;
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value;
                OnPropertyChanged();
            }
        }
        public T Value { get; }

        public OptionItem(string text, T value)
        {
            _text = text;
            Value = value;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override string ToString() => _text;
    }
}
