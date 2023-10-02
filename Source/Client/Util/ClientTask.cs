using System;
using System.Runtime.CompilerServices;
using Verse;

namespace Multiplayer.Client.Util;

[AsyncMethodBuilder(typeof(ClientTaskMethodBuilder))]
public struct ClientTask : INotifyCompletion
{
    public bool IsCompleted => false;
    public void GetResult(){}
    public void OnCompleted(Action continuation){}
    public ClientTask GetAwaiter() => this;
}

public struct ClientTaskMethodBuilder
{
    public static ClientTaskMethodBuilder Create() => new();

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine =>
        stateMachine.MoveNext();

    public void SetStateMachine(IAsyncStateMachine stateMachine) { }

    public void SetException(Exception exception)
    {
        Log.Error($"Multiplayer ClientTask exception: {exception}");
    }

    public void SetResult(){}

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        awaiter.OnCompleted(stateMachine.MoveNext);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        awaiter.UnsafeOnCompleted(stateMachine.MoveNext);
    }

    public ClientTask Task { get; }
}
