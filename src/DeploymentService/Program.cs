using Vertica.Integration.WebApi;

namespace Vertica.DeploymentService
{
    class Program
    {
        static void Main(string[] args)
        {
            IntegrationStartup.Run(args, application => application
                .Database(database => database.IntegrationDb(integrationDb => integrationDb.Disable()))
                .Tasks(tasks => tasks.Clear())
                .Hosts(hosts => hosts.Clear().Host<DeploymentHelperHost>())
                .UseWebApi(webApi => webApi
                    .AddToLiteServer()
                    .AddFromAssemblyOfThis<Program>()));
        }
    }
}
