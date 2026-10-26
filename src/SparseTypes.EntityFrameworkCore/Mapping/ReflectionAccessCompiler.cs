using System.Linq.Expressions;
using System.Reflection;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>Compiles reflection access while preserving target invocation exceptions.</summary>
internal static class ReflectionAccessCompiler
{
    internal static TDelegate Compile<TDelegate>(Expression<TDelegate> expression)
        where TDelegate : Delegate
    {
        var exception = Expression.Parameter(typeof(Exception), "exception");
        var wrapped = Expression.New(typeof(TargetInvocationException).GetConstructor([typeof(Exception)])!, exception);
        var body = Expression.TryCatch(expression.Body, Expression.Catch(exception, Expression.Throw(wrapped, expression.ReturnType)));
        return Expression.Lambda<TDelegate>(body, expression.Parameters).Compile();
    }

    internal static Func<T> CreateFactory<T>(ConstructorInfo constructor)
    {
        // Abstract navigations can use existing instances; fail only if creation is requested.
        if (constructor.DeclaringType!.IsAbstract)
        {
            return () => (T)constructor.Invoke(null);
        }

        return Compile(Expression.Lambda<Func<T>>(Expression.Convert(Expression.New(constructor), typeof(T))));
    }
}
