using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Dispatching;
using Xunit;

namespace JsonRpc.Tests
{
    public class RequestDispatcherTests
    {
        [Fact]
        public async Task PipelinesWrapHandlerInRegistrationOrder()
        {
            var calls = new List<string>();
            var services = Services();
            services.AddSingleton<IRequestHandler<Message, string>>(new Handler<Message, string>((_, _) => {
                calls.Add("handler");
                return Task.FromResult("response");
            }));
            services.AddSingleton<IPipelineBehavior<Message, string>>(new Behavior<Message, string>(async (_, next, _) => {
                calls.Add("first-before");
                var result = await next();
                calls.Add("first-after");
                return result + "-first";
            }));
            services.AddSingleton<IPipelineBehavior<Message, string>>(new Behavior<Message, string>(async (_, next, _) => {
                calls.Add("second-before");
                var result = await next();
                calls.Add("second-after");
                return result + "-second";
            }));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.Equal("response-second-first", await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message()));
            Assert.Equal(new[] { "first-before", "second-before", "handler", "second-after", "first-after" }, calls);
        }

        [Fact]
        public async Task PipelineCanShortCircuitWithoutResolvingHandler()
        {
            var services = Services();
            services.AddSingleton<IPipelineBehavior<Message, string>>(new Behavior<Message, string>((_, _, _) => Task.FromResult("cached")));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.Equal("cached", await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message()));
        }

        [Fact]
        public async Task OpenGenericPipelinesAlsoWrapUnitNotifications()
        {
            var calls = new List<string>();
            var services = Services();
            services.AddSingleton(calls);
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RecordingBehavior<,>));
            services.AddSingleton<IRequestHandler<Notice, Unit>>(new Handler<Notice, Unit>((_, _) => {
                calls.Add("notification");
                return Unit.Task;
            }));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.Equal(Unit.Value, await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Notice()));
            Assert.Equal(new[] { "before", "notification", "after" }, calls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ContinuationRetainsOrReplacesCancellationToken(bool replace)
        {
            using var initial = new CancellationTokenSource();
            using var replacement = new CancellationTokenSource();
            var expected = replace ? replacement.Token : initial.Token;
            var services = Services();
            services.AddSingleton<IPipelineBehavior<Message, string>>(new Behavior<Message, string>((_, next, token) => {
                Assert.Equal(initial.Token, token);
                return replace ? next(replacement.Token) : next();
            }));
            services.AddSingleton<IPipelineBehavior<Message, string>>(new Behavior<Message, string>((_, next, token) => {
                Assert.Equal(expected, token);
                return next();
            }));
            services.AddSingleton<IRequestHandler<Message, string>>(new Handler<Message, string>((_, token) => {
                Assert.Equal(expected, token);
                return Task.FromResult("ok");
            }));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.Equal("ok", await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message(), initial.Token));
        }

        [Fact]
        public async Task ExplicitNoneTokenDoesNotRetainOriginalToken()
        {
            using var initial = new CancellationTokenSource();
            var services = Services();
            services.AddSingleton<IPipelineBehavior<Message, string>>(new Behavior<Message, string>((_, next, _) => next(CancellationToken.None)));
            services.AddSingleton<IRequestHandler<Message, string>>(new Handler<Message, string>((_, token) => {
                Assert.Equal(CancellationToken.None, token);
                return Task.FromResult("ok");
            }));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message(), initial.Token);
        }

        [Fact]
        public async Task AlreadyCanceledRequestDoesNotEnterPipeline()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var services = Services();
            services.AddSingleton<IPipelineBehavior<Message, string>>(new Behavior<Message, string>((_, _, _) => throw new InvalidOperationException("must not run")));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message(), cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }

        [Fact]
        public async Task CancellationDuringPipelinePreventsHandlerInvocation()
        {
            using var cancellation = new CancellationTokenSource();
            var services = Services();
            services.AddSingleton<IPipelineBehavior<Message, string>>(new Behavior<Message, string>((_, next, _) => {
                cancellation.Cancel();
                return next();
            }));
            services.AddSingleton<IRequestHandler<Message, string>>(new Handler<Message, string>((_, _) => throw new InvalidOperationException("must not run")));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message(), cancellation.Token));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task HandlerExceptionsAreNotWrapped(bool asynchronous)
        {
            var expected = new InvalidOperationException("handler failure");
            var services = Services();
            services.AddSingleton<IRequestHandler<Message, string>>(new Handler<Message, string>((_, _) =>
                asynchronous ? Task.FromException<string>(expected) : throw expected));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message()));
            Assert.Same(expected, actual);
        }

        [Fact]
        public async Task ConcurrentDispatchUsesSelectedDescriptorInEachDryIocScope()
        {
            using var container = JsonRpcServerContainer.Create(null).AddJsonRpcDispatcher();
            var services = new ServiceCollection();
            services.AddScoped(_ => new List<string>());
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RecordingBehavior<,>));
            container.Populate(services);
            var scopeFactory = container.Resolve<IServiceScopeFactory>();
            using var first = scopeFactory.CreateScope();
            using var second = scopeFactory.CreateScope();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetDescriptor(first.ServiceProvider, "first");
            SetDescriptor(second.ServiceProvider, "second");

            var firstTask = first.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message());
            var secondTask = second.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message());
            Assert.False(firstTask.IsCompleted);
            Assert.False(secondTask.IsCompleted);
            gate.SetResult(true);

            Assert.Equal(new[] { "first", "second" }, await Task.WhenAll(firstTask, secondTask));
            Assert.NotSame(first.ServiceProvider.GetRequiredService<IRequestContext>(), second.ServiceProvider.GetRequiredService<IRequestContext>());

            void SetDescriptor(IServiceProvider provider, string response)
            {
                var descriptor = Substitute.For<IHandlerDescriptor>();
                descriptor.Handler.Returns(new Handler<Message, string>(async (_, _) => {
                    await gate.Task;
                    return response;
                }));
                provider.GetRequiredService<IRequestContext>().Descriptor = descriptor;
            }
        }

        [Fact]
        public async Task ScopedHandlersAreDisposedAndNeverCached()
        {
            var services = Services();
            services.AddScoped<IRequestHandler<Message, string>, ScopedHandler>();
            using var provider = services.BuildServiceProvider();
            string firstResponse;
            ScopedHandler firstHandler;
            using (var first = provider.CreateScope())
            {
                firstHandler = (ScopedHandler) first.ServiceProvider.GetRequiredService<IRequestHandler<Message, string>>();
                firstResponse = await first.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message());
            }
            Assert.True(firstHandler.Disposed);
            using var second = provider.CreateScope();
            Assert.NotEqual(firstResponse, await second.ServiceProvider.GetRequiredService<IRequestDispatcher>().Send(new Message()));
        }

        [Fact]
        public void ContractsDoNotReferenceExternalMediatorAssemblies()
        {
            Assert.DoesNotContain(typeof(IRequest<>).Assembly.GetReferencedAssemblies(), name =>
                name.Name!.StartsWith("MediatR", StringComparison.OrdinalIgnoreCase));
        }

        private static ServiceCollection Services()
        {
            var services = new ServiceCollection();
            services.AddScoped<IRequestDispatcher, RequestDispatcher>();
            return services;
        }

        public class Message : IRequest<string> { }
        public class Notice : IRequest { }

        private sealed class Handler<TRequest, TResponse> : IJsonRpcRequestHandler<TRequest, TResponse>
            where TRequest : IRequest<TResponse>
        {
            private readonly Func<TRequest, CancellationToken, Task<TResponse>> _handle;
            public Handler(Func<TRequest, CancellationToken, Task<TResponse>> handle) => _handle = handle;
            public Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken) => _handle(request, cancellationToken);
        }

        private sealed class Behavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
        {
            private readonly Func<TRequest, RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>> _handle;
            public Behavior(Func<TRequest, RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>> handle) => _handle = handle;
            public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
                _handle(request, next, cancellationToken);
        }

        public sealed class RecordingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
        {
            private readonly List<string> _calls;
            public RecordingBehavior(List<string> calls) => _calls = calls;
            public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
            {
                _calls.Add("before");
                var result = await next();
                _calls.Add("after");
                return result;
            }
        }

        public sealed class ScopedHandler : IRequestHandler<Message, string>, IDisposable
        {
            private readonly string _identity = Guid.NewGuid().ToString();
            public bool Disposed { get; private set; }
            public Task<string> Handle(Message request, CancellationToken cancellationToken) => Task.FromResult(_identity);
            public void Dispose() => Disposed = true;
        }
    }
}
