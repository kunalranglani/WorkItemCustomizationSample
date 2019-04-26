using CommandLine;

namespace WorkItemCustomizationSample
{
    public class Options
    {
        [Option('a', "account", HelpText = "The url of the Azure Devops account like https://dev.azure.com/fabrikam", Required = true)]
        public string AccountUrl { get; set; }

        [Option('p', "project", HelpText = "The name of the project", Required = true)]
        public string ProjectName { get; set; }
    }
}
