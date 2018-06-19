using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CoreRPC.Binding.Default;
using CoreRPC.Routing;
using CoreRPC.Serialization;
using CoreRPC.Transferable;
using CoreRPC.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace CoreRPC.AspNetCore
{
    public static class CoreRpcAspNetCoreExtensions
    {
        class Request : IRequest
        {
            private readonly HttpContext _ctx;

            public Request(HttpContext ctx, byte[] data)
            {
                _ctx = ctx;
                Data = data;
            }
            public byte[] Data { get; }
            public object Context => _ctx;

            public Task RespondAsync(byte[] data) => _ctx.Response.Body.WriteAsync(data, 0, data.Length);
        }

        static async Task Handle(HttpContext context, IRequestHandler handler)
        {
            var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms);
            await handler.HandleRequest(new Request(context, ms.ToArray()));
        }

        public static IApplicationBuilder UseCoreRpc(this IApplicationBuilder builder, PathString path,
            IRequestHandler handler)
            => builder.Use((context, next) =>
            {
                if (context.Request.Path == path && context.Request.Method == "POST")
                    return Handle(context, handler);
                return next();
            });

        public static IApplicationBuilder UseCoreRpc(this IApplicationBuilder builder, PathString path,
            Action<CoreRpcAspNetCoreConfiguration> configure = null)
        {
            var cfg = new CoreRpcAspNetCoreConfiguration();
            configure?.Invoke(cfg);
            var engine = new Engine(new JsonMethodCallSerializer(cfg.JsonSerializer), new DefaultMethodBinder());
            var env = builder.ApplicationServices.GetRequiredService<IHostingEnvironment>();
            var types = RpcTypesResolver.GetRpcTypes(env);
            var extractor = new AspNetCoreTargetNameExtractor();
            var selector = new DefaultTargetSelector(new AspNetCoreTargetFactory(), extractor);
            foreach (var t in types)
                selector.Register(extractor.GetTargetName(t), t);
            return builder.UseCoreRpc(path, engine.CreateRequestHandler(selector, new CallInterceptor(cfg.Interceptors)));
        }

        class CallInterceptor : IMethodCallInterceptor
        {
            delegate Task<object> Interceptor(MethodCall call, object context, Func<Task<object>> invoke);
            private readonly List<Interceptor> _chain;

            public CallInterceptor(IEnumerable<IMethodCallInterceptor> chain)
            {
                _chain = chain.Select(i => (Interceptor) i.Intercept).Append(DoIntercept).ToList();
            }

            public Task<object> Intercept(MethodCall call, object context, Func<Task<object>> invoke)
            {
                if (_chain.Count == 1)
                    return DoIntercept(call, context, invoke);
                var current = -1;
                Task<object> Invoke()
                {
                    current++;
                    if (current >= _chain.Count)
                        return invoke();
                    return _chain[current](call, context, Invoke);
                }
                return Invoke();
            }
            
            Task<object> DoIntercept(MethodCall call, object context, Func<Task<object>> invoke)
            {
                if (call.Target is IHttpContextAwareRpc aware)
                    return aware.OnExecuteRpcCall((HttpContext) context, invoke);
                return invoke();
            }
        }
    }

    public class CoreRpcAspNetCoreConfiguration
    {
        public JsonSerializer JsonSerializer { get; } = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = {new StringEnumConverter()}
        };
        public List<IMethodCallInterceptor> Interceptors { get; } = new List<IMethodCallInterceptor>();
    }
}
