using System;
using System.Collections.Generic;

namespace DeluxDriver;

/// <summary>
/// 极简 DI 容器：仅支持单例注册/解析，满足 Phase 3 的 ViewModel/Service 注入需求。
/// 不引入第三方容器，保持依赖最小。
/// </summary>
public class ServiceContainer
{
    private readonly Dictionary<Type, object> _singletons = new();
    private readonly Dictionary<Type, Func<object>> _factories = new();

    public void AddSingleton<T>(T instance) where T : class
    {
        _singletons[typeof(T)] = instance;
    }

    public void AddSingleton<T>(Func<T> factory) where T : class
    {
        _factories[typeof(T)] = () => factory();
    }

    public void AddSingleton<T>() where T : class
    {
        _singletons[typeof(T)] = null!; // 延迟构造（需有无参构造函数）
    }

    public T GetRequiredService<T>() where T : class
    {
        if (_singletons.TryGetValue(typeof(T), out var existing) && existing != null)
            return (T)existing;

        if (_factories.TryGetValue(typeof(T), out var factory))
        {
            var instance = factory();
            _singletons[typeof(T)] = instance;
            return (T)instance;
        }

        // 延迟构造：仅支持无参构造
        var created = (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;
        _singletons[typeof(T)] = created;
        return created;
    }
}
