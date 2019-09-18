using System;
using System.Threading.Tasks;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;
using GitHub.Primitives;
using Microsoft.Alm.Authentication;
using McMaster.Extensions.CommandLineUtils;

namespace Git_GitHub
{
    class Program
    {
        [Option("--host", Description = "The host URL")]
        public string Host { get; } = "https://github.com";

        public static Task Main(string[] args)
            => CommandLineApplication.ExecuteAsync<Program>(args);

        public async Task OnExecute()
        {
            var productInformation = new ProductHeaderValue("Git-GitHub", "0.1");
            var token = GetToken(Host);

            var hostAddress = HostAddress.Create(Host);
            var connection = new Connection(productInformation, hostAddress.GraphQLUri, token);

            await ShowCreatedPullRequests(connection);
        }

        async Task ShowCreatedPullRequests(Connection connection)
        {
            var orderBy = new IssueOrder { Field = IssueOrderField.CreatedAt, Direction = OrderDirection.Desc };
            var openPullRequests = new[] { PullRequestState.Open };

            var states = new[] { PullRequestState.Open };
            var query = new Query()
                .Viewer
                .PullRequests(100, null, null, null, null, null, null, orderBy, states)
                .Nodes
                .Select(pr => new { pr.Repository.NameWithOwner, pr.Title, pr.Number, pr.Author.Login, pr.CreatedAt })
                .Compile();

            var result = await connection.Run(query);

            foreach (var pr in result)
            {
                Console.WriteLine(
@$"{pr.NameWithOwner} - {pr.Title}
#{pr.Number} opened on {pr.CreatedAt:D} by {pr.Login}
");
            }
        }

        static string GetToken(string url)
        {
            var secrets = new SecretStore("git");
            var auth = new BasicAuthentication(secrets);
            var remoteUri = new Uri(url);
            var targetUrl = remoteUri.GetLeftPart(UriPartial.Authority);
            var creds = auth.GetCredentials(new TargetUri(targetUrl));
            if (creds is null)
            {
                throw new ApplicationException($"Couldn't find credentials for {targetUrl}");
            }

            return creds.Password;
        }
    }
}
