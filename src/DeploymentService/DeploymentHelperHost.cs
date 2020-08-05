using System;
using System.Collections.Generic;
using System.Reflection;
using System.ServiceProcess;
using Vertica.Integration;
using Vertica.Integration.Domain.LiteServer;
using Vertica.Integration.Infrastructure;
using Vertica.Integration.Infrastructure.Extensions;
using Vertica.Integration.Infrastructure.IO;
using Vertica.Integration.Infrastructure.Windows;
using Vertica.Integration.Model.Hosting;
using Vertica.Integration.Model.Hosting.Handlers;
using Vertica.Integration.WebApi.Infrastructure;
using Vertica.Utilities.Extensions.StringExt;

namespace Vertica.DeploymentService
{
	public class DeploymentHelperHost : IHost
	{
	    private readonly IWindowsServiceHandler _windowsServiceHandler;
		private readonly IWindowsServices _windowsServices;
		private readonly ILiteServerFactory _liteServerFactory;
	    private readonly IConsoleWriter _consoleWriter;
	    private readonly IShutdown _shutdown;
	    private readonly IHttpServerFactory _httpServerFactory;
        private readonly IRuntimeSettings _settings;

		public DeploymentHelperHost(
            IWindowsFactory windows, ILiteServerFactory liteServerFactory,
            IConsoleWriter consoleWriter, IShutdown shutdown, IHttpServerFactory httpServerFactory,
            IWindowsServiceHandler windowsServiceHandler, IRuntimeSettings settings)
		{
	        _shutdown = shutdown;
	        _httpServerFactory = httpServerFactory;
	        _windowsServiceHandler = windowsServiceHandler;
            _settings = settings;
            _liteServerFactory = liteServerFactory;
		    _consoleWriter = consoleWriter;
		    _windowsServices = windows.WindowsServices();
		}

        private string WindowsServiceName
        {
            get
            {
                string serviceName = _settings["WindowsService.Name"]?.Trim().NullIfEmpty();

                return serviceName ?? "VerticaDeploymentService";
            }
        }

        private string WindowsServiceDisplayName
        {
            get
			{
                string displayName = _settings["WindowsService.DisplayName"]?.Trim().NullIfEmpty();

                return displayName ?? "Vertica Deployment Service";
			}
        }


		public bool CanHandle(HostArguments args)
		{
			return true;
		}

		public void Handle(HostArguments args)
		{
			if (RunningAsWindowsService())
				return;

			if (UninstallWindowsService(args))
				return;

			if (RunDevelopmentMode(args))
				return;

			EnsureWindowsServiceIsInstalledAndStarted();
		}

		public string Description => $"Windows Service that can assist in various deployment scenarious. Listens to: {WebApiUrl}.";

	    private string WebApiUrl
	    {
	        get
	        {
                string url = _httpServerFactory.GetOrGenerateUrl(out bool basedOnSetting);

	            if (!basedOnSetting)
	                throw new InvalidOperationException(@"You need to specify a WebApi url - do that in the app.config:
<add key=""WebApi.Url"" value=""http://localhost:10993"" />");

	            return url;
	        }
	    }

		private void EnsureWindowsServiceIsInstalledAndStarted()
		{
			if (_windowsServices.Exists(WindowsServiceName))
			{
                ServiceControllerStatus serviceStatus = _windowsServices.GetStatus(WindowsServiceName);

                if (serviceStatus != ServiceControllerStatus.Running)
                {
                    _consoleWriter.WriteLine($"Starting windows service '{WindowsServiceDisplayName}' ('{WindowsServiceName}')");

                    _windowsServices.Start(WindowsServiceName);

                    serviceStatus = _windowsServices.GetStatus(WindowsServiceName);
                }

                _consoleWriter.WriteLine($"Windows service '{WindowsServiceDisplayName}' ('{WindowsServiceName}') has status {serviceStatus}.");
				return;
			}

			_consoleWriter.WriteLine($"Installing '{WindowsServiceDisplayName}' ('{WindowsServiceName}') as windows service");
			_consoleWriter.WriteLine();

			var configuration = new WindowsServiceConfiguration(WindowsServiceName, ExePath, "-service")
				.DisplayName(WindowsServiceDisplayName)
				.Description(Description)
				.StartMode(ServiceStartMode.Automatic);

			_windowsServices.Install(configuration);

			try
			{
				_windowsServices.Start(WindowsServiceName);

                _consoleWriter.WriteLine($"Windows service '{WindowsServiceDisplayName}' ('{WindowsServiceName}') started.");
			}
			catch (Exception ex)
			{
				_consoleWriter.WriteLine();
				_consoleWriter.WriteLine("NOTICE: Attempted to start the windows service, but failed with: ");
				_consoleWriter.WriteLine();
				_consoleWriter.WriteLine(ex.ToString());
			}
		}

	    private bool UninstallWindowsService(HostArguments args)
		{
			bool windowsServiceExists = _windowsServices.Exists(WindowsServiceName);

			if (CommandIs(args, "uninstall"))
			{
				if (windowsServiceExists)
				{
					_windowsServices.Stop(WindowsServiceName);
					_windowsServices.Uninstall(WindowsServiceName);
				}

				return true;
			}

			return false;
		}

		private static bool CommandIs(HostArguments args, string command)
		{
			return
				string.Equals(args.Command, command, StringComparison.OrdinalIgnoreCase) ||
				args.CommandArgs.Contains(command);
		}

		private bool RunDevelopmentMode(HostArguments args)
		{
			if (CommandIs(args, "development"))
			{
				using (Starter())
				{
					_shutdown.WaitForShutdown();
					return true;
				}
			}

			return false;
		}

		private bool RunningAsWindowsService()
		{
			if (!Environment.UserInteractive)
			{
			    var commandArgs = new[] {new KeyValuePair<string, string>("service", string.Empty)};
			    var args = new KeyValuePair<string, string>[0];

                _windowsServiceHandler.Handle(
                    new HostArguments(this.Name(), commandArgs, args),
			        new HandleAsWindowsService(WindowsServiceName, WindowsServiceDisplayName, Description, Starter));

			    _consoleWriter.WriteLine("An instance is already running as a Windows Service listening on Url: {0}", WebApiUrl);

				return true;
			}
			
			return false;
		}

		private Func<IDisposable> Starter => () => _liteServerFactory.Create();

		private static string ExePath => Assembly.GetEntryAssembly()?.Location;
	}
}