using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using Polly;
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

	    [HttpGet]
	    [Route("status")]
	    public IHttpActionResult GetStatus([FromUri] string serviceName = null)
	    {
	        if (string.IsNullOrWhiteSpace(serviceName))
	            return BadRequest($"Missing required value for querystring '{nameof(serviceName)}'.");

            bool exists = _windowsServices.Exists(serviceName);

            if (!exists)
                return NotFound();

	        return Ok(_windowsServices.GetStatus(serviceName));
	    }
        
        [HttpPut]
	    [Route("status")]
	    public IHttpActionResult UpdateStatus([FromUri] string serviceName = null, [FromUri] bool? start = null)
	    {
            if (string.IsNullOrWhiteSpace(serviceName))
                return BadRequest($"Missing required value for querystring '{nameof(serviceName)}'.");

            if (start == null)
                return BadRequest($"Missing required value for querystring '{nameof(start)}'.");

            bool exists = _windowsServices.Exists(serviceName);

            if (!exists)
                return NotFound();

            if (start.Value)
            {
                _windowsServices.Start(serviceName);
            }
            else
            {
                _windowsServices.Stop(serviceName);
            }

            return Ok(_windowsServices.GetStatus(serviceName));
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

			var directory = new DirectoryInfo(localDirectory);

			if (!directory.Exists)
			    directory.Create();

			using (Stream stream = firstFile.ReadAsStreamAsync().Result)
			using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
			{
                bool exists = _windowsServices.Exists(serviceName);

			    if (exists)
			    {
			        //The service might take more than 10 seconds to stop. Retry a few times, if that happens.
			        Policy
			            .Handle<System.ServiceProcess.TimeoutException>()
			            .WaitAndRetry(new[]
			            {
			                TimeSpan.FromSeconds(5),
			                TimeSpan.FromSeconds(10),
			                TimeSpan.FromSeconds(15)
			            })
			            .Execute(() =>
			            {
			                _windowsServices.Stop(serviceName, TimeSpan.FromSeconds(10));
                        });
			    }

                var backup = new DirectoryInfo($@"{directory.Parent?.FullName}\Backups");
                backup.Create();

                // TODO: Delete backups older than XX-days

                ZipArchive[] zipCopy = { zip };

                Policy
                    .Handle<IOException>()
                    .WaitAndRetry(new[]
                    {
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(15),
                        TimeSpan.FromSeconds(20)
                    })
                    .Execute(() =>
                    {
                        directory.MoveTo($@"{backup.FullName}\Backup_{directory.Name}_{DateTime.Now:yyyyMMddHHmmss}");
                        ZipFile.CreateFromDirectory(directory.FullName, $@"{backup.FullName}\{directory.Name}.zip");
                        directory.Delete(recursive: true);
                        zipCopy[0].ExtractToDirectory(localDirectory);
                    });

			    if (exists)
			    {
                    try
                    {
                        //The service might take more than the 30 seconds to start that the OS expects. Retry a few times, if that happens.
                        Policy
                            .Handle<InvalidOperationException>(ex =>
                                ex.InnerException is Win32Exception
                                && ex.InnerException?.Message.Contains("The service did not respond to the start or control request in a timely fashion") == true)
                            .WaitAndRetry(new[]
                            {
                                TimeSpan.FromSeconds(5),
                                TimeSpan.FromSeconds(10),
                                TimeSpan.FromSeconds(15)
                            })
                            .Execute(() =>
                            {
                                _windowsServices.Start(serviceName);
                            });
                    }
                    catch (Exception ex)
                    {
                        return BadRequest($"Service deployed but service '{serviceName}' failed to start: {ex}");
                    }
                }
			}

		    return Ok(_windowsServices.GetStatus(serviceName));
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