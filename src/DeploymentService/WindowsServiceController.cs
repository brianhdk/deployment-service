using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Web.Http;
using Vertica.Integration.Infrastructure.Windows;

namespace Vertica.DeploymentService
{
	[RoutePrefix("windowsservice")]
	public class WindowsServiceController : ApiController
	{
		private readonly IWindowsServices _windowsServices;

		public WindowsServiceController(IWindowsFactory windows)
		{
			_windowsServices = windows.WindowsServices();
		}

		[HttpPost]
		[Route("")]
		public IHttpActionResult Install([FromUri] string serviceName, [FromUri] string localDirectory)
		{
			var streamProvider = new MultipartMemoryStreamProvider();
			Request.Content.ReadAsMultipartAsync(streamProvider).Wait();

			HttpContent firstFile = streamProvider.Contents.FirstOrDefault();

			if (firstFile == null)
				return BadRequest("Missing file.");

			bool exists = _windowsServices.Exists(serviceName);

			if (!exists)
				return BadRequest($"Service with name '{serviceName}' does not exist.");

			var directory = new DirectoryInfo(localDirectory);

			if (!directory.Exists)
				return BadRequest($"Local Directory '{localDirectory}' was not found.");

			using (Stream stream = firstFile.ReadAsStreamAsync().Result)
			{
				using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
				{
					_windowsServices.Stop(serviceName);

					// Allow the OS time to have locks removed from the files
                    // TODO: Figure out whether we can improve on this - programatically detect file-locks
					Thread.Sleep(TimeSpan.FromSeconds(30));

					try
					{
						var backup = new DirectoryInfo($@"{directory.Parent?.FullName}\Backups");
						backup.Create();

						directory.MoveTo($@"{backup.FullName}\Backup_{directory.Name}_{DateTime.Now:yyyyMMddHHmmss}");
						ZipFile.CreateFromDirectory(directory.FullName, $@"{backup.FullName}\{directory.Name}.zip");
						directory.Delete(recursive: true);

						zip.ExtractToDirectory(localDirectory);
					}
					catch
					{
						// If deployment fails - start it again.
						_windowsServices.Start(serviceName);

						throw;
					}

					try
					{
						_windowsServices.Start(serviceName);
					}
					catch (Exception ex)
					{
						return BadRequest($"Service deployed but service failed to start: {ex}");
					}	
				}
			}

			return Ok();
		}

		[HttpGet]
		[Route("start")]
		public IHttpActionResult Start(string serviceName)
		{
			_windowsServices.Start(serviceName);

			return Ok();
		}

		[HttpGet]
		[Route("stop")]
		public IHttpActionResult Stop(string serviceName)
		{
			_windowsServices.Stop(serviceName);

			return Ok();
		}
	}
}