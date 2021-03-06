﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Jaeger;
using Jaeger.Reporters;
using Jaeger.Samplers;
using Jaeger.Senders;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTracing;
using OpenTracing.Contrib.NetCore.CoreFx;
using OpenTracing.Util;
using Serilog;

namespace otsample.web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .ConfigureLogging((context, logging) =>
                {
                    logging.AddSerilog(
                        new LoggerConfiguration()
                            .ReadFrom.Configuration(context.Configuration)
                            .Enrich.FromLogContext()
                            .CreateLogger());
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddLogging();
                    services.Configure<TracerOptions>(context.Configuration.GetSection("tracer"));
                    services.AddSingleton<ITracer>(serviceProvider =>
                    {
                        var tracerOptions = serviceProvider.GetRequiredService<IOptions<TracerOptions>>();
                        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                    
                        var serviceName = tracerOptions.Value?.ServiceName ?? Assembly.GetEntryAssembly().GetName().Name;
                        var sampler = new ConstSampler(sample: true);
                        var mode = tracerOptions.Value?.Mode ?? TracerMode.Udp;
                    
                        var tracer = new Tracer.Builder(serviceName)
                            .WithReporter(
                                new RemoteReporter.Builder()
                                    .WithLoggerFactory(loggerFactory)
                                    .WithSender(
                                        mode == TracerMode.Http ?
                                        (ISender)new HttpSender(tracerOptions.Value?.HttpEndPoint ?? "http://localhost:14268/api/traces") :
                                        new UdpSender(tracerOptions.Value?.UdpEndPoint?.Host ?? "localhost", tracerOptions.Value?.UdpEndPoint?.Port ?? 6831, 0))
                                    .Build())
                            .WithLoggerFactory(loggerFactory)
                            .WithSampler(sampler)
                            .Build();
                    
                        GlobalTracer.Register(tracer);
                    
                        // Prevent endless loops when OpenTracing is tracking HTTP requests to Jaeger.
                        services.Configure<HttpHandlerDiagnosticOptions>(options =>
                        {
                            options.IgnorePatterns.Add(request => new Uri(tracerOptions.Value?.HttpEndPoint ?? "http://localhost:14268/api/traces").IsBaseOf(request.RequestUri));
                        });
                    
                        return tracer;
                    });
                    services.AddOpenTracing();                                        
                })
                .UseSerilog();
    }
}
