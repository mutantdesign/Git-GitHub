using System;
using System.Threading.Tasks;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;
using GitHub.Primitives;
using Microsoft.Alm.Authentication;
using McMaster.Extensions.CommandLineUtils;
using System.Linq;

namespace Git_GitHub
{
    [Command("git-github")]
    [Subcommand(
        typeof(PullsCommand),
        typeof(IssuesCommand),
        typeof(ViewerCommand),
        typeof(RepositoriesCommand))]
    class Program : GitHubCommandBase
    {
        public static Task Main(string[] args)
            => CommandLineApplication.ExecuteAsync<Program>(args);

        protected override Task OnExecute(CommandLineApplication app)
        {
            // this shows help even if the --help option isn't specified
            app.ShowHelp();
            return Task.CompletedTask;
        }
    }

    [Command(Description = "Show pull requests")]
    class PullsCommand : GitHubCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

            var orderBy = new IssueOrder { Field = IssueOrderField.CreatedAt, Direction = OrderDirection.Desc };
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
    }

    [Command(Description = "Show issues")]
    class IssuesCommand : GitHubCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

            var orderBy = new IssueOrder { Field = IssueOrderField.CreatedAt, Direction = OrderDirection.Desc };
            var states = new[] { IssueState.Open };
            var query = new Query()
                .Viewer
                .Issues(100, null, null, null, null, null, orderBy, states)
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
    }

    [Command(Description = "Show viewer information")]
    class ViewerCommand : GitHubCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

            var query = new Query()
                .Viewer
                .Select(v =>
                new
                {
                    v.Login,
                    v.Name,
                    // Requires 'user:email' or 'read:user' scopes
                    // v.Email, 
                    // Requires 'read:org' scope
                    // Organizations = v.Organizations(100, null, null, null).Nodes.Select(o => new { o.Login, o.Name }).ToList()
                })
                .Compile();

            var result = await connection.Run(query);

            Console.WriteLine($"You are signed in as {result.Login} ({result.Name})");
            //Console.WriteLine(@"Organizations:");
            //Console.WriteLine(string.Join('\n', result.Organizations.Select(o => $"{o.Login} ({o.Name})")));
        }
    }

    [Command(Description = "List repositories")]
    class RepositoriesCommand : GitHubCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

            var owner = Owner;
            if(owner is null)
            {
                var loginQuery = new Query()
                    .Viewer
                    .Select(v => v.Login)
                    .Compile();

                owner = await connection.Run(loginQuery);
            }

            var query = new Query()
                .RepositoryOwner(owner)
                .Repositories(first: 100)
                .Nodes
                .Select(r => new { r.Name, r.IsPrivate, ForkedFrom = r.Parent != null ? r.Parent.NameWithOwner : null })
                .Compile();

            var result = await connection.Run(query);

            foreach (var r in result)
            {
                Console.WriteLine($"{r.Name}{(r.IsPrivate ? " [Private]" : "")}{(r.ForkedFrom != null ? " Forked from " + r.ForkedFrom : "")}");
            }
        }

        [Option("--owner", Description = "The owning user or organization")]
        public string Owner { get; }
    }

    /// <summary>
    /// This base type provides shared functionality.
    /// Also, declaring <see cref="HelpOptionAttribute"/> on this type means all types that inherit from it
    /// will automatically support '--help'
    /// </summary>
    [HelpOption("--help")]
    abstract class GitHubCommandBase
    {
        protected abstract Task OnExecute(CommandLineApplication app);

        protected Connection CreateConnection()
        {
            var productInformation = new ProductHeaderValue("Git-GitHub", "0.1");
            var token = GetToken(Host);

            var hostAddress = HostAddress.Create(Host);
            var connection = new Connection(productInformation, hostAddress.GraphQLUri, token);
            return connection;
        }

        protected static string GetToken(string url)
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

        [Option("--host", Description = "The host URL")]
        public string Host { get; } = "https://github.com";
    }
}
