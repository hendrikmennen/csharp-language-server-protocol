using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace OmniSharp.Extensions.JsonRpc.Dispatching
{
    internal sealed class RequestDispatcher : IRequestDispatcher
    {
        private readonly IServiceProvider _services;

        public RequestDispatcher(IServiceProvider services) => _services = services;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            return Invokers<TResponse>.Cache.GetOrAdd(
                request.GetType(),
                type => (IInvoker<TResponse>) Activator.CreateInstance(typeof(Invoker<,>).MakeGenericType(type, typeof(TResponse)))!
            ).Invoke(_services, request, cancellationToken);
        }

        // Cache only the type adapter, never handlers or services belonging to a routing scope.
        private static class Invokers<TResponse>
        {
            internal static readonly ConcurrentDictionary<Type, IInvoker<TResponse>> Cache = new();
        }

        private interface IInvoker<TResponse>
        {
            Task<TResponse> Invoke(IServiceProvider services, IRequest<TResponse> request, CancellationToken cancellationToken);
        }

        private sealed class Invoker<TRequest, TResponse> : IInvoker<TResponse> where TRequest : IRequest<TResponse>
        {
            public Task<TResponse> Invoke(IServiceProvider services, IRequest<TResponse> request, CancellationToken cancellationToken)
            {
                var message = (TRequest) request;
                var behaviors = services.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();
                return InvokeAt(0, cancellationToken);

                Task<TResponse> InvokeAt(int index, CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();
                    if (index == behaviors.Length)
                    {
                        return services.GetRequiredService<IRequestHandler<TRequest, TResponse>>().Handle(message, token);
                    }

                    return behaviors[index].Handle(message, replacement => InvokeAt(index + 1, replacement ?? token), token);
                }
            }
        }
    }
}
