using Vertica.Integration;
using Vertica.Integration.Domain.LiteServer;
using Vertica.Integration.WebApi;

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
                    .AddToLiteServer()
                    .AddFromAssemblyOfThis<Program>())))
			{
				context.Execute(args);
			}
        }
    }
}
