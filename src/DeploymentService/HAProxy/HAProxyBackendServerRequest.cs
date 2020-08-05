namespace Vertica.DeploymentService.HAProxy
{
    public class HAProxyBackendServerRequest : HAProxyRequest
    {
        public string Name { get; set; }

        public string BackendName { get; set; }

        public override bool Validate(out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                errorMessage = $"Missing required value for querystring '{nameof(Name)}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(BackendName))
            {
                errorMessage = $"Missing required value for querystring '{nameof(BackendName)}'.";
                return false;
            }

            return base.Validate(out errorMessage);
        }
    }
}