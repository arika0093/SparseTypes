using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>Caches conversions from generated fragments to one DTO type.</summary>
internal sealed class SparseSnapshotConverter<TDto>
    where TDto : class
{
    private readonly ConditionalWeakTable<Type, Converter> _converters = new();

    private sealed class Converter
    {
        internal Converter(Type type)
        {
            var toModel = type.GetMethod("ToModel", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (toModel is null || toModel.ContainsGenericParameters || !typeof(TDto).IsAssignableFrom(toModel.ReturnType))
            {
                return;
            }

            var value = Expression.Parameter(typeof(object), "value");
            var target = type.IsValueType ? Expression.Unbox(value, type) : Expression.Convert(value, type);
            var call = Expression.Call(target, toModel);
            Convert = ReflectionAccessCompiler.Compile(Expression.Lambda<Func<object, TDto?>>(Expression.Convert(call, typeof(TDto)), value));
        }

        internal Func<object, TDto?>? Convert { get; }
    }

    internal bool TryConvert(object? value, out TDto? snapshot)
    {
        if (value is TDto direct)
        {
            snapshot = direct;
            return true;
        }

        snapshot = value is null ? null : _converters.GetValue(value.GetType(), static type => new Converter(type)).Convert?.Invoke(value);
        return snapshot is not null;
    }
}
