using System;
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

namespace Vertica.DeploymentService
{
	[RoutePrefix("windowsservice")]
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

			var streamProvider = new MultipartMemoryStreamProvider();
			await Request.Content.ReadAsMultipartAsync(streamProvider, token);

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
                await StopService(token, serviceName);

                await BackupPreviousVersion(token, directory, backup);

                // Extract the new version
                zip.ExtractToDirectory(localDirectory);

                try
                {
                    _windowsServices.Start(serviceName);
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
                    directory.Delete(recursive: true);
                }

                CleanupPreviousBackups(backup);
            }

		    return Ok(_windowsServices.GetStatus(serviceName));
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

        private static Task BackupPreviousVersion(CancellationToken token, DirectoryInfo directory, DirectoryInfo backup)
        {
            return Policy
                .Handle<IOException>()
                .WaitAndRetryAsync(new[]
                {
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(20)
                })
                .ExecuteAsync(ct =>
                {
                    // Move the current version to the backup folder
                    directory.MoveTo($@"{backup.FullName}\Backup_{directory.Name}_{DateTime.Now:yyyyMMddHHmmss}");

                    return Task.CompletedTask;

                }, token);
        }

        private Task StopService(CancellationToken token, string serviceName)
        {
            return Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(new[]
                {
                    TimeSpan.FromSeconds(5)
                })
                .ExecuteAsync(ct =>
                {
                    // Stop the existing service
                    _windowsServices.Stop(serviceName, TimeSpan.FromSeconds(10));

                    return Task.CompletedTask;
                }, token);
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