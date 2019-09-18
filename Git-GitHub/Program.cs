using System;
using System.Threading.Tasks;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;
using Microsoft.Alm.Authentication;

namespace Git_GitHub
{
    class Program
    {
        static async Task Main()
        {
            var productInformation = new ProductHeaderValue("Git-GitHub", "0.1");
            var token = GetToken("https://github.com");
            var connection = new Connection(productInformation, token);

            await ShowCreatedPullRequests(connection);
        }

        static async Task ShowCreatedPullRequests(Connection connection)
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
