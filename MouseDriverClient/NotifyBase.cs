using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MouseDriverClient
{
    /// <summary>
    /// 通知基类：所有需要被 XAML 绑定的配置模型/视图模型都继承它，
    /// 自动属性改为带通知的属性（<c>Set</c> 模板），UI 改动即回写模型。
    /// </summary>
    public abstract class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnChanged(name);
            return true;
        }

        protected void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
