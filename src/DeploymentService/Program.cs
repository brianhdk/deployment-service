using System;
using System.IO;
using Vertica.Integration;
using Vertica.Integration.Domain.LiteServer;
using Vertica.Integration.WebApi;
using Vertica.Utilities.Extensions.StringExt;

namespace Vertica.DeploymentService
{
    class Program
    {
        static void Main(string[] args)
        {
            using (IApplicationContext context = ApplicationContext.Create(application => application
                .Database(database => database
                    .IntegrationDb(integrationDb => integrationDb
                        .Disable()))
                .Tasks(tasks => tasks
                    .Clear())
                .Hosts(hosts => hosts
                    .Clear()
                    .Host<DeploymentHelperHost>())
                .UseLiteServer(liteServer => liteServer
                    .AddFromAssemblyOfThis<Program>())
                .UseWebApi(webApi => webApi
                    .HttpServer(httpServer =>  httpServer.Configure(configuration =>
                    {
                        if (string.Equals(bool.TrueString, configuration.Kernel.Resolve<IRuntimeSettings>()["LogRequests.Enabled"], StringComparison.OrdinalIgnoreCase))
                        {
                            string baseDirectory = 
                                configuration.Kernel.Resolve<IRuntimeSettings>()["LogRequests.Directory"].NullIfEmpty() ??
                                // ReSharper disable once AssignNullToNotNullAttribute
                                Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "Requests");

                            Directory.CreateDirectory(baseDirectory);

                            configuration.Http.MessageHandlers.Add(new LogRequestAndResponseHandler(baseDirectory));
                        }
                    }))
                    .AddToLiteServer()
                    .AddFromAssemblyOfThis<Program>())))
			{
				context.Execute(args);
			}
        }
    }
}
