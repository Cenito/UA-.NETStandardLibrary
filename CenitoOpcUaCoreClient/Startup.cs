using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpcUa;

namespace CenitoOpcUaCoreClient
{
    public class Startup
    {
        OpcUaDriver m_OpcUADriver;
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            if (env.IsEnvironment("Development"))
            {
                // This will push telemetry data through Application Insights pipeline faster, allowing you to view results immediately.
                builder.AddApplicationInsightsSettings(developerMode: true);
            }

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddApplicationInsightsTelemetry(Configuration);

            services.AddMvc();

            m_OpcUADriver = new OpcUaDriver();
            var endpointURL = $"opc.tcp://192.168.33.22:4840";

            m_OpcUADriver.InitializeClient(endpointURL, GetAddresses());
        }

        public IEnumerable<string> GetAddresses()
        {
            var addresses = new List<string>() {
                            "::AsGlobalPV:gAOT.HMI.QT201_50.Value",
                            "::AsGlobalPV:gMain.HMI.CertifiedFlow",
                            "::AsGlobalPV:gMain.HMI.FT201_1.Value" ,
                            "::AsGlobalPV:gFilter.HMI.PT201_72.Value",
                            "::AsGlobalPV:gAOT.IO.WritePower.SP",
                            "::AsGlobalPV:gFilter.HMI.M703_1.Cmd",
                            "::AsGlobalPV:gMain.HMI.Pf",
                            "::AsGlobalPV:gMain.HMI.Heartbeat.HMICounter",
                            "::AsGlobalPV:gMain.HMI.V201_8.Value",
                            "::AsGlobalPV:gMain.HMI.PT201_16.Value",
                            "::AsGlobalPV:gMain.HMI.ProcessedVolume",
                            "::AsGlobalPV:gAOT.HMI.DynamicPower",
                            "::AsGlobalPV:gMain.HMI.PIDStatus2"
            };
            return addresses;
        }


        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseApplicationInsightsRequestTelemetry();

            app.UseApplicationInsightsExceptionTelemetry();

            app.UseMvc();
        }
    }
}
