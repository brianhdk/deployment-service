using System;
using Vertica.Integration;
using Vertica.Integration.ConsoleHost;

namespace Vertica.DeploymentService
{
	public static class IntegrationStartup
	{
		public static void Run(string[] args, Action<ApplicationConfiguration> application = null)
		{
			using (IApplicationContext context = ApplicationContext.Create(cfg => cfg
				.UseConsoleHost()
				.Change(application)))
			{
				context.Execute(args);
			}
		}
	}
}