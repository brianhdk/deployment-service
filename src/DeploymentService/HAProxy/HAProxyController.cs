using System;
using System.Web.Http;
using HAProxyApi.Client;
using HAProxyApi.Client.Models;

namespace Vertica.DeploymentService.HAProxy
{
    [RoutePrefix("haProxy")]
    public class HAProxyController : ApiController
    {
        [HttpGet]
        [Route("status")]
        public IHttpActionResult GetStatus([FromUri] HAProxyRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (!request.Validate(out string errorMessage))
                return BadRequest(errorMessage);

            HaProxyClient client = request.CreateClient();

            return Ok(new
            {
                Backends = client.ShowBackends(),
                Servers = client.ShowBackendServers(),
                Info = client.ShowInfo(),
                Errors = client.ShowErrors()
            });
        }

        [HttpGet]
        [Route("server")]
        public IHttpActionResult GetServer([FromUri] HAProxyBackendServerRequest request)
        {
            return PerformBackendServerAction(request, client => 
                client.ShowBackendServer(request.Name, request.BackendName));
        }

        [HttpGet]
        [Route("server/start")]
        public IHttpActionResult StartServer([FromUri] HAProxyBackendServerRequest request)
        {
            return PerformBackendServerAction(request, client => 
                client.EnableServer(request.Name, request.BackendName));
        }

        [HttpGet]
        [Route("server/stop")]
        public IHttpActionResult StopServer([FromUri] HAProxyBackendServerRequest request)
        {
            return PerformBackendServerAction(request, client => 
                client.DisableServer(request.Name, request.BackendName));
        }

        private IHttpActionResult PerformBackendServerAction(HAProxyBackendServerRequest request, Func<HaProxyClient, BackendServer> action)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!request.Validate(out string errorMessage))
                return BadRequest(errorMessage);

            HaProxyClient client = request.CreateClient();

            BackendServer backendServer = action(client);

            if (backendServer == null)
                return NotFound();

            return Ok(backendServer);
        }
    }
}