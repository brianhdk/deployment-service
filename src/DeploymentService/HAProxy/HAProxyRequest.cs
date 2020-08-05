using System;
using HAProxyApi.Client;

namespace Vertica.DeploymentService.HAProxy
{
    public class HAProxyRequest
    {
        public string HostNameOrIpAddress { get; set; }

        public int? PortNumber { get; set; }

        public virtual bool Validate(out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(HostNameOrIpAddress))
            {
                errorMessage = $"Missing required value for querystring '{nameof(HostNameOrIpAddress)}'.";
                return false;
            }

            if (!PortNumber.HasValue)
            {
                errorMessage = $"Missing required value for querystring '{nameof(PortNumber)}'.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public HaProxyClient CreateClient()
        {
            if (string.IsNullOrWhiteSpace(HostNameOrIpAddress))
                throw new InvalidOperationException($"Missing required value for '{nameof(HostNameOrIpAddress)}'.");

            if (!PortNumber.HasValue)
                throw new InvalidOperationException($"Missing required value for '{nameof(PortNumber)}'.");

            return new HaProxyClient(HostNameOrIpAddress, PortNumber.Value);
        }
    }
}