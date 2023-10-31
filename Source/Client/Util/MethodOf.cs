using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;

namespace Multiplayer.Client.Util;

public static class MethodOf
{
    /// <summary>Given a lambda expression that calls a method, returns the method info</summary>
    /// <param name="expression">The lambda expression using the method</param>
    /// <returns>The method in the lambda expression</returns>
    ///
    public static MethodInfo Inner(Expression<Action> expression)
    {
        return Inner((LambdaExpression)expression);
    }

    /// <summary>Given a lambda expression that calls a method, returns the method info</summary>
    /// <typeparam name="T">The generic type</typeparam>
    /// <param name="expression">The lambda expression using the method</param>
    /// <returns>The method in the lambda expression</returns>
    ///
    public static MethodInfo Inner<T>(Expression<Action<T>> expression)
    {
        return Inner((LambdaExpression)expression);
    }

    /// <summary>Given a lambda expression that calls a method, returns the method info</summary>
    /// <typeparam name="T">The generic type</typeparam>
    /// <typeparam name="TResult">The generic result type</typeparam>
    /// <param name="expression">The lambda expression using the method</param>
    /// <returns>The method in the lambda expression</returns>
    ///
    public static MethodInfo Inner<T, TResult>(Expression<Func<T, TResult>> expression)
    {
        return Inner((LambdaExpression)expression);
    }

    /// <summary>Given a lambda expression that calls a method, returns the method info</summary>
    /// <param name="expression">The lambda expression using the method</param>
    /// <returns>The method in the lambda expression</returns>
    ///
    public static MethodInfo Inner(LambdaExpression expression)
    {
        if (expression.Body is not MethodCallExpression outermostExpression)
        {
            if (expression.Body is UnaryExpression ue && ue.Operand is MethodCallExpression me &&
                me.Object is ConstantExpression ce && ce.Value is MethodInfo mi)
                return mi;
            throw new ArgumentException("Invalid Expression. Expression should consist of a Method call only.");
        }

        var method = outermostExpression.Method;
        if (method is null)
            throw new Exception($"Cannot find method for expression {expression}");

        return method;
    }

    public static MethodInfo Lambda(Delegate del)
    {
        return del.Method;
    }

    public static HarmonyMethod Harmony(this MethodInfo m)
    {
        return new HarmonyMethod(m);
    }
}
