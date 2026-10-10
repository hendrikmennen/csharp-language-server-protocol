using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using NSubstitute;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using Xunit;
using Xunit.Abstractions;
using Arg = NSubstitute.Arg;
using Request = OmniSharp.Extensions.JsonRpc.Server.Request;

namespace JsonRpc.Tests
{
    public class RequestRouterTests : AutoTestBase
    {
        public RequestRouterTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper) => Container = JsonRpcTestContainer.Create(testOutputHelper);

        [Fact]
        public async Task ShouldRoute_CustomRequestResponse()
        {
            var collection = new HandlerCollection(Substitute.For<IResolverContext>(), new AssemblyScanningHandlerTypeDescriptorProvider(new [] { typeof(AssemblyScanningHandlerTypeDescriptorProvider).Assembly, typeof(HandlerResolverTests).Assembly }));
            var registry = new TestLanguageServerRegistry();
            AutoSubstitute.Provide<IHandlersManager>(collection);
            AutoSubstitute.Provide<IEnumerable<IHandlerDescriptor>>(collection);
            var mediator = AutoSubstitute.Resolve<RequestRouter>();
            var method = Substitute.For<Func<string, Task<long>>>();
            method.Invoke(Arg.Any<string>()).Returns(1000L);
            registry.OnRequest("$/my/something/awesome", method);

            registry.Populate(collection, ServiceProvider);

            var request = new Request(Guid.NewGuid().ToString(), "$/my/something/awesome", "123123123123");
            var response = await mediator.RouteRequest(mediator.GetDescriptors(request), request, CancellationToken.None);
            Assert.False(response.IsError);
            Assert.Equal(1000L, ((JToken) response.Response!.Result!).Value<long>());

            await method.Received(1).Invoke(Arg.Any<string>());
        }

        [Fact]
        public async Task ShouldRoute_CustomRequest()
        {
            var collection = new HandlerCollection(Substitute.For<IResolverContext>(), new AssemblyScanningHandlerTypeDescriptorProvider(new [] { typeof(AssemblyScanningHandlerTypeDescriptorProvider).Assembly, typeof(HandlerResolverTests).Assembly }));
            var registry = new TestLanguageServerRegistry();
            AutoSubstitute.Provide<IHandlersManager>(collection);
            AutoSubstitute.Provide<IEnumerable<IHandlerDescriptor>>(collection);
            var mediator = AutoSubstitute.Resolve<RequestRouter>();
            var method = Substitute.For<Func<string, Task>>();
            method.Invoke(Arg.Any<string>()).Returns(Task.CompletedTask);
            registry.OnRequest("$/my/something/awesome", method);

            registry.Populate(collection, ServiceProvider);

            var request = new Request(Guid.NewGuid().ToString(), "$/my/something/awesome", "123123123123");
            await mediator.RouteRequest(mediator.GetDescriptors(request), request, CancellationToken.None);

            await method.Received(1).Invoke(Arg.Any<string>());
        }

        [Fact]
        public async Task ShouldRoute_CustomNotification()
        {
            var collection = new HandlerCollection(Substitute.For<IResolverContext>(), new AssemblyScanningHandlerTypeDescriptorProvider(new [] { typeof(AssemblyScanningHandlerTypeDescriptorProvider).Assembly, typeof(HandlerResolverTests).Assembly }));
            var registry = new TestLanguageServerRegistry();
            AutoSubstitute.Provide<IHandlersManager>(collection);
            AutoSubstitute.Provide<IEnumerable<IHandlerDescriptor>>(collection);
            var mediator = AutoSubstitute.Resolve<RequestRouter>();
            var method = Substitute.For<Action<string>>();
            registry.OnNotification("$/my/something/awesome", method);

            registry.Populate(collection, ServiceProvider);

            var notification = new Notification("$/my/something/awesome", "123123123123");
            await mediator.RouteNotification(mediator.GetDescriptors(notification), notification, CancellationToken.None);

            method.Received(1).Invoke(Arg.Any<string>());
        }

        [Fact]
        public async Task ShouldRoute_CustomEmptyNotification()
        {
            var collection = new HandlerCollection(Substitute.For<IResolverContext>(), new AssemblyScanningHandlerTypeDescriptorProvider(new [] { typeof(AssemblyScanningHandlerTypeDescriptorProvider).Assembly, typeof(HandlerResolverTests).Assembly }));
            var registry = new TestLanguageServerRegistry();
            AutoSubstitute.Provide<IHandlersManager>(collection);
            AutoSubstitute.Provide<IEnumerable<IHandlerDescriptor>>(collection);
            var mediator = AutoSubstitute.Resolve<RequestRouter>();
            var method = Substitute.For<Action>();
            registry.OnNotification("$/my/something/awesome", method);

            registry.Populate(collection, ServiceProvider);

            var notification = new Notification("$/my/something/awesome", null);
            await mediator.RouteNotification(mediator.GetDescriptors(notification), notification, CancellationToken.None);

            method.Received(1).Invoke();
        }

        [Fact]
        public async Task ShouldMapUnitResponseToNull()
        {
            var handler = Substitute.For<MediatorTestsRequestHandlerOfTRequest.IExecuteCommandHandler>();
            handler.Handle(Arg.Any<MediatorTestsRequestHandlerOfTRequest.ExecuteCommandParams>(), Arg.Any<CancellationToken>())
                   .Returns(OmniSharp.Extensions.JsonRpc.Dispatching.Unit.Task);
            var collection = new HandlerCollection(
                Substitute.For<IResolverContext>(),
                new AssemblyScanningHandlerTypeDescriptorProvider(new[] { typeof(HandlerResolverTests).Assembly })
            ) { handler };
            AutoSubstitute.Provide<IHandlersManager>(collection);
            var router = AutoSubstitute.Resolve<RequestRouter>();
            var request = new Request("unit", "workspace/executeCommand", JObject.FromObject(new { Command = "test" }));

            var response = await router.RouteRequest(router.GetDescriptors(request), request, CancellationToken.None);

            Assert.False(response.IsError);
            Assert.Null(response.Response!.Result);
        }

        [Fact]
        public async Task ShouldPropagateSynchronousHandlerExceptionWithoutReflectionWrapper()
        {
            var expected = new InvalidOperationException("handler failed");
            var handler = Substitute.For<MediatorTestsRequestHandlerOfTRequest.IExecuteCommandHandler>();
            handler.Handle(Arg.Any<MediatorTestsRequestHandlerOfTRequest.ExecuteCommandParams>(), Arg.Any<CancellationToken>())
                   .Returns<Task<OmniSharp.Extensions.JsonRpc.Dispatching.Unit>>(_ => throw expected);
            var collection = new HandlerCollection(
                Substitute.For<IResolverContext>(),
                new AssemblyScanningHandlerTypeDescriptorProvider(new[] { typeof(HandlerResolverTests).Assembly })
            ) { handler };
            AutoSubstitute.Provide<IHandlersManager>(collection);
            var router = AutoSubstitute.Resolve<RequestRouter>();
            var request = new Request("error", "workspace/executeCommand", JObject.FromObject(new { Command = "test" }));

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                router.RouteRequest(router.GetDescriptors(request), request, CancellationToken.None));

            Assert.Same(expected, actual);
        }
    }
}
