using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SparseTypes.Blazor;

/// <summary>Compiles boxed property readers when dynamic code is available.</summary>
internal static class SparsePropertyReader
{
    internal static Func<object, object?> Create(PropertyInfo property)
    {
        var getter = property.GetMethod!;
        if (!RuntimeFeature.IsDynamicCodeSupported || getter.ReturnType.IsByRef || getter.ReturnType.IsByRefLike || getter.ReturnType.IsPointer)
        {
            return property.GetValue;
        }

        var value = Expression.Parameter(typeof(object), "value");
        var declaringType = getter.DeclaringType!;
        var target = declaringType.IsValueType ? Expression.Unbox(value, declaringType) : Expression.Convert(value, declaringType);
        var call = Expression.Convert(Expression.Call(target, getter), typeof(object));
        var exception = Expression.Parameter(typeof(Exception), "exception");
        var wrapped = Expression.New(typeof(TargetInvocationException).GetConstructor([typeof(Exception)])!, exception);
        var body = Expression.TryCatch(call, Expression.Catch(exception, Expression.Throw(wrapped, typeof(object))));
        return Expression.Lambda<Func<object, object?>>(body, value).Compile();
    }
}
