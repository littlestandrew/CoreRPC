﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AsyncRpc.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using AsyncRpc.Transport.Http;
using Microsoft.AspNetCore.Http;

namespace Tests
{
	class AspCoreTests
	{
		class Startup : IRequestHandler
		{
			public Startup(IHostingEnvironment env)
			{
				
			}

			public void ConfigureServices(IServiceCollection services)
			{
				
			}

			public void Configure(IApplicationBuilder app, IHostingEnvironment env)
			{
				app.UseAsyncRpc("/rpc", this);
			}


			public Task HandleRequest(IRequest req) => req.RespondAsync(req.Data.Reverse().ToArray());
		}

		[Fact]
		public void AspNetCoreShouldBeOperational()
		{
			var builder = new WebHostBuilder()
				.UseKestrel()
				.UseStartup<Startup>();
			var host = builder.Build();
			host.Start();
			var address = host.ServerFeatures.Get<IServerAddressesFeature>();
			var addr = address.Addresses.ToString();
			var transport = new HttpClientTransport(addr+"/rpc");
			var resp = transport.SendMessageAsync(new byte[] {1, 2}).Result;
			Assert.True(resp.SequenceEqual(new byte[] {2, 1}));
			host.Services.GetService<IApplicationLifetime>().StopApplication();
		}

	}
}