using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmniSharp.Extensions.JsonRpc.Dispatching
{
    /// <summary>Identifies a protocol message and its response type.</summary>
    public interface IRequest<out TResponse>
    {
    }

    /// <summary>Identifies a message with no response payload.</summary>
    public interface IRequest : IRequest<Unit>
    {
    }

    /// <summary>Handles a protocol message within its routing scope.</summary>
    public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Continues dispatch, retaining the current token unless one is supplied.</summary>
    public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken? cancellationToken = null);

    /// <summary>Wraps a handler. Behaviors execute in registration order, outermost first.</summary>
    public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull
    {
        Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
    }

    /// <summary>Dispatches requests and notifications (whose response is <see cref="Unit"/>).</summary>
    public interface IRequestDispatcher
    {
        Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    }

    /// <summary>The response of a message with no result. It is serialized as JSON null by the router.</summary>
    public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>
    {
        public static readonly Unit Value = default;
        public static Unit Default => Value;
        public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(Value);

        public bool Equals(Unit other) => true;
        public override bool Equals(object? obj) => obj is Unit;
        public override int GetHashCode() => 0;
        public int CompareTo(Unit other) => 0;
        public override string ToString() => "()";
        public static bool operator ==(Unit left, Unit right) => true;
        public static bool operator !=(Unit left, Unit right) => false;
    }
}
