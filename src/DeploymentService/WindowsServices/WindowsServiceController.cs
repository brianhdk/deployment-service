using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Polly;
using Vertica.Integration;
using Vertica.Integration.Infrastructure.Windows;

namespace Vertica.DeploymentService.WindowsServices
{
	[RoutePrefix("windowsService")]
	public class WindowsServiceController : ApiController
	{
        private readonly IRuntimeSettings _settings;
        private readonly IWindowsServices _windowsServices;

		public WindowsServiceController(IWindowsFactory windows, IRuntimeSettings settings)
        {
            _settings = settings;
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
        public async Task<IHttpActionResult> Install(CancellationToken token, [FromUri] string serviceName = null, [FromUri] string localDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                return BadRequest($"Missing required value for querystring '{nameof(serviceName)}'.");

            if (string.IsNullOrWhiteSpace(localDirectory))
                return BadRequest($"Missing required value for querystring '{nameof(localDirectory)}'.");

            if (!_windowsServices.Exists(serviceName))
                return BadRequest($"Service '{serviceName}' does not exist.");

            if (!Directory.Exists(localDirectory))
                return BadRequest($"Directory '{localDirectory}' does not exist.");

            if (!Request.Content.IsMimeMultipartContent("form-data"))
            {
                return BadRequest($@"Request is invalid. Content is not MIME multipart content. 
{string.Join(Environment.NewLine, Request.Headers.Select(x => $"{x.Key}: {string.Join(", ", x.Value)}"))}
{string.Join(Environment.NewLine, Request.Content.Headers.Select(x => $"{x.Key}: {string.Join(", ", x.Value)}"))}");
            }

            var streamProvider = await Request.Content.ReadAsMultipartAsync(new MultipartMemoryStreamProvider(), token);

            HttpContent firstFile = streamProvider.Contents.FirstOrDefault();

            if (firstFile == null)
                return BadRequest("Missing required file in request.");

            var directory = new DirectoryInfo(localDirectory);

            // Ensure backup folder
            var backup = new DirectoryInfo($@"{directory.Parent?.FullName}\Backups");
            backup.Create();

            using (Stream stream = await firstFile.ReadAsStreamAsync())
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                await StopService(serviceName, token);

                // Move the current version to the backup folder
                await Wrap(() => directory.MoveTo($@"{backup.FullName}\Backup_{directory.Name}_{DateTime.Now:yyyyMMddHHmmss}"), token);

                // Extract the new version
                zip.ExtractToDirectory(localDirectory);

                try
                {
                    await StartService(serviceName, token);
                }
                catch (Exception ex)
                {
                    return BadRequest($"Service deployed but service '{serviceName}' failed to start: {ex}");
                }
                finally
                {
                    // Zip the folder with the previous version
                    ZipFile.CreateFromDirectory(directory.FullName, $@"{backup.FullName}\{directory.Name}.zip");

                    // Delete the folder
                    await Wrap(() => directory.Delete(recursive: true), token);
                }

                CleanupPreviousBackups(backup);
            }

            return Ok(_windowsServices.GetStatus(serviceName));
        }

        [HttpGet]
		[Route("start")]
		public async Task<IHttpActionResult> Start(CancellationToken cancellationToken, [FromUri] string serviceName = null)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                return BadRequest($"Missing required value for querystring '{nameof(serviceName)}'.");

            if (!_windowsServices.Exists(serviceName))
                return BadRequest($"Service '{serviceName}' does not exist.");

            await StartService(serviceName, cancellationToken);

            return Ok();
        }

        [HttpGet]
		[Route("stop")]
		public async Task<IHttpActionResult> Stop(CancellationToken cancellationToken, [FromUri] string serviceName = null)
		{
            if (string.IsNullOrWhiteSpace(serviceName))
                return BadRequest($"Missing required value for querystring '{nameof(serviceName)}'.");

            if (!_windowsServices.Exists(serviceName))
                return BadRequest($"Service '{serviceName}' does not exist.");

            await StopService(serviceName, cancellationToken);

			return Ok();
		}

        private Task StartService(string serviceName, CancellationToken cancellationToken)
        {
            return Policy
                .Handle<System.ServiceProcess.TimeoutException>()
                .OrInner<Win32Exception>(ex => ex.Message.Contains("The service did not respond to the start or control request in a timely fashion"))
                .WaitAndRetryAsync(new[]
                {
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(15)
                })
                .ExecuteAsync(ct =>
                {
                    _windowsServices.Start(serviceName);

                    return Task.CompletedTask;

                }, cancellationToken);
        }

        private Task StopService(string serviceName, CancellationToken cancellationToken)
        {
            return Policy
                .Handle<System.ServiceProcess.TimeoutException>()
                .WaitAndRetryAsync(new[]
                {
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(15)
                })
                .ExecuteAsync(ct =>
                {
                    _windowsServices.Stop(serviceName, TimeSpan.FromSeconds(10));

                    return Task.CompletedTask;

                }, cancellationToken);
        }

        private void CleanupPreviousBackups(DirectoryInfo backup)
        {
            if (!int.TryParse(_settings["Backup.PreserveNumberOfVersions"], out int preserveCount) || preserveCount < 0)
                preserveCount = 5;

            // Ensure only keeping X backups for each localDirectory
            FileInfo[] backupFilesToDelete = backup
                .GetFiles("*.zip")
                .OrderByDescending(x => x.CreationTime)
                .Select(x => (File: x, Match: Regex.Match(x.Name, "^Backup_(?<Name>[^_]+)_.*$")))
                .Where(x => x.Match.Success)
                .GroupBy(x => x.Match.Groups["Name"].Value, StringComparer.OrdinalIgnoreCase)
                .SelectMany(x => x.Skip(preserveCount).Select(y => y.File))
                .ToArray();

            foreach (FileInfo backupFile in backupFilesToDelete)
                backupFile.Delete();
        }

        private static Task Wrap(Action ioAction, CancellationToken token)
        {
            return Policy
                .Handle<IOException>()
                .Or<UnauthorizedAccessException>()
                .WaitAndRetryAsync(new[]
                {
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(20)
                })
                .ExecuteAsync(ct =>
                {
                    ioAction();

                    return Task.CompletedTask;

                }, token);
        }
    }
}