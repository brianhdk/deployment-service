Hi, and thanks for trying out the Integration Service platform.
To get started, modify your Program.cs as shown below to begin working with this.

Also make sure to visit the Project Home on https://github.com/vertica-as/integration-service for further documentation.

class Program
{
    static void Main(string[] args)
    {
        IntegrationStartup.Run(args);
    }
}