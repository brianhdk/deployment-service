using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Vertica.DeploymentService
{
    public class LogRequestAndResponseHandler : DelegatingHandler
    {
        private readonly string _baseDirectory;

        public LogRequestAndResponseHandler(string baseDirectory)
        {
            _baseDirectory = baseDirectory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string id = Guid.NewGuid().ToString("N");

            string requestBody = await request.Content.ReadAsStringAsync();

            File.WriteAllText(Path.Combine(_baseDirectory, $"{id}-request.txt"), requestBody);

            HttpResponseMessage result = await base.SendAsync(request, cancellationToken);

            if (result.Content != null)
            {
                string responseBody = await result.Content.ReadAsStringAsync();

                File.WriteAllText(Path.Combine(_baseDirectory, $"{id}-response.txt"), responseBody);
            }

            return result;
        }
    }
}