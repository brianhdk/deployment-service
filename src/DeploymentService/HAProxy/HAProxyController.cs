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
        public IHttpActionResult GetStatus([FromUri] HAProxyRequest request = null)
        {
            if (!IsValid(request, out IHttpActionResult invalidResult))
                return invalidResult;

            if (request == null)
                throw new InvalidOperationException();

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
        public IHttpActionResult GetServer([FromUri] HAProxyBackendServerRequest request = null)
        {
            return PerformBackendServerAction(request, client => 
                client.ShowBackendServer(request?.BackendName, request?.Name));
        }

        [HttpGet]
        [Route("server/start")]
        public IHttpActionResult StartServer([FromUri] HAProxyBackendServerRequest request = null)
        {
            return PerformBackendServerAction(request, client => 
                client.EnableServer(request?.BackendName, request?.Name));
        }

        [HttpGet]
        [Route("server/stop")]
        public IHttpActionResult StopServer([FromUri] HAProxyBackendServerRequest request = null)
        {
            return PerformBackendServerAction(request, client => 
                client.DisableServer(request?.BackendName, request?.Name));
        }

        private IHttpActionResult PerformBackendServerAction(HAProxyBackendServerRequest request, Func<HaProxyClient, BackendServer> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (!IsValid(request, out IHttpActionResult invalidResult))
                return invalidResult;

            if (request == null)
                throw new InvalidOperationException();

            HaProxyClient client = request.CreateClient();

            BackendServer backendServer = action(client);

            if (backendServer == null)
                return NotFound();

            return Ok(backendServer);
        }

        private bool IsValid(HAProxyRequest request, out IHttpActionResult invalidResult)
        {
            if (request == null)
            {
                invalidResult = BadRequest(HAProxyRequest.HostNameOrIpAddressRequiredValidationMessage);
                return false;
            }

            if (!request.Validate(out string errorMessage))
            {
                invalidResult = BadRequest(errorMessage);
                return false;
            }

            invalidResult = null;
            return true;
        }
    }
}