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
        typeof(OrganizationsCommand),        
        typeof(RepositoriesCommand),
        typeof(BranchCommand),
        typeof(UpstreamCommand),
        typeof(LoginCommand),
        typeof(LogoutCommand))]
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
                .PullRequests(first: 100, orderBy: orderBy, states: states)
                .Nodes
                .Select(pr => new { Repository = pr.Repository.NameWithOwner, pr.Title, pr.Number, Author = pr.Author != null ? pr.Author.Login : null, pr.CreatedAt })
                .Compile();

            var result = await connection.Run(query);

            foreach (var pr in result)
            {
                Console.WriteLine(
@$"{pr.Repository} - {pr.Title}
#{pr.Number} opened on {pr.CreatedAt:D} by {pr.Author}
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
                .Issues(first: 100, orderBy: orderBy, states: states)
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
                    v.Name
                })
                .Compile();

            var result = await connection.Run(query);

            Console.WriteLine($"You are signed in as {result.Login} ({result.Name})");
        }
    }

    [Command(Description = "Show visible organizations (requires 'read:org' scope)")]
    class OrganizationsCommand : GitHubCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

            var query = new Query()
                .Viewer
                .Organizations(first: 100)
                .Nodes
                .Select(o => new
                {
                    o.Login,
                    Repositories = o.Repositories(null, null, null, null, null, null, null, null, null, null).TotalCount,
                    Teams = o.Teams(null, null, null, null, null, null, null, null, null, null, null).TotalCount,
                    Members = o.MembersWithRole(null, null, null, null).TotalCount,
                    Projects = o.Projects(null, null, null, null, null, null, null).TotalCount
                })
                .Compile();

            var result = await connection.Run(query);

            foreach (var o in result)
            {
                Console.WriteLine($"{o.Login} has {o.Repositories} repositories, {o.Members} members, {o.Teams} teams and {o.Projects}");
            }
        }
    }

    [Command(Description = "List repositories")]
    class RepositoriesCommand : GitHubCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var connection = CreateConnection();

            var owner = Owner;
            if (owner is null)
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

    [Command(Description = "Show information about the current branch")]
    class BranchCommand : GitHubCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var gitDirectory = LibGit2Sharp.Repository.Discover(".");
            using (var repository = new LibGit2Sharp.Repository(gitDirectory))
            {
                var head = repository.Head;
                var trackedBranch = head.TrackedBranch;
                if (trackedBranch is null)
                {
                    Console.WriteLine($"Current branch '{head.FriendlyName}' isn't tracking a remote.");
                    return;
                }

                var upstreamBranchCanonicalName = trackedBranch.UpstreamBranchCanonicalName;
                var branchName = ToBranchName(upstreamBranchCanonicalName);
                var remoteUrl = new UriString(repository.Network.Remotes[trackedBranch.RemoteName].Url);

                var pullRequestStates = new[] { PullRequestState.Open, PullRequestState.Closed, PullRequestState.Merged };
                var query = new Query()
                    .Repository(owner: remoteUrl.Owner, name: remoteUrl.RepositoryName)
                    .Ref(upstreamBranchCanonicalName)
                    .Select(r => new
                    {
                        Repository = r.Repository.NameWithOwner,
                        ForkedFrom = r.Repository.Parent != null ? r.Repository.Parent.NameWithOwner : null,
                        r.Target.Oid,
                        PullRequests = r.AssociatedPullRequests(100, null, null, null, null, branchName, null, null, pullRequestStates).Nodes.Select(pr => new
                        {
                            Repository = pr.Repository.NameWithOwner,
                            pr.Number,
                            pr.Title,
                            pr.Url,
                            Author = pr.Author != null ? pr.Author.Login : null,
                            pr.CreatedAt,
                            pr.AuthorAssociation,
                            pr.State,
                            pr.HeadRefName,
                            HeadRepository = pr.HeadRepository.NameWithOwner,
                            pr.BaseRefName,
                            BaseRepository = pr.BaseRef != null ? pr.BaseRef.Repository.NameWithOwner : null,
                            pr.HeadRefOid
                        }).ToList()
                    }).Compile();

                var connection = CreateConnection(remoteUrl);
                var result = await connection.Run(query);

                Console.WriteLine(result.ForkedFrom is null ? result.Repository : $"{result.Repository} forked from {result.ForkedFrom}");
                Console.WriteLine(result.Oid == trackedBranch.Tip.Sha ? "No new commits" : "There are new commits!");
                var prs = result.PullRequests
                    .Where(pr => pr.HeadRepository == pr.Repository); // Only show incoming pull requests
                if (prs.Count() == 0)
                {
                    Console.WriteLine("No associated pull requests");
                }
                else
                {
                    Console.WriteLine(@"
Associated pull requests:");
                    foreach (var pr in prs)
                    {
                        Console.WriteLine(
        @$"{pr.Repository} - {pr.Title} [{pr.State}]
#{pr.Number} opened on {pr.CreatedAt:D} by {pr.Author} ({pr.AuthorAssociation})");
                    }
                    Console.WriteLine();
                }
            }
        }

        static string ToBranchName(string canonicalName)
        {
            var prefix = "refs/heads/";
            if (canonicalName.StartsWith(prefix))
            {
                return canonicalName.Substring(prefix.Length);
            }

            return null;
        }
    }

    [Command(Description = "Show information about the upstream repository")]
    class UpstreamCommand : GitHubCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var gitDirectory = LibGit2Sharp.Repository.Discover(".");
            using (var repository = new LibGit2Sharp.Repository(gitDirectory))
            {
                var remote = repository.Network.Remotes.FirstOrDefault();
                if (remote is null)
                {
                    Console.WriteLine("This repository contains no remotes");
                    return;
                }

                var remoteUrl = new UriString(remote.Url);

                var openPullRequestState = new[] { PullRequestState.Open };
                var openIssueState = new[] { IssueState.Open };
                var query = new Query()
                    .Repository(owner: remoteUrl.Owner, name: remoteUrl.RepositoryName)
                    .Select(r => new
                    {
                        Repository = r.Select(p => new
                        {
                            p.NameWithOwner,
                            p.ViewerPermission,
                            DefaultBranchName = p.DefaultBranchRef.Name,
                            OpenPullRequests = p.PullRequests(null, null, null, null, null, null, null, null, openPullRequestState).TotalCount,
                            OpenIssues = p.Issues(null, null, null, null, null, null, null, openIssueState).TotalCount
                        }).Single(),
                        Parent = r.Parent == null ? null : r.Parent.Select(p => new
                        {
                            p.NameWithOwner,
                            p.ViewerPermission,
                            DefaultBranchName = p.DefaultBranchRef.Name,
                            OpenPullRequests = p.PullRequests(null, null, null, null, null, null, null, null, openPullRequestState).TotalCount,
                            OpenIssues = p.Issues(null, null, null, null, null, null, null, openIssueState).TotalCount
                        }).Single()
                    }).Compile();

                var connection = CreateConnection(remoteUrl);
                var result = await connection.Run(query);

                if (result.Parent != null)
                {
                    Console.WriteLine($"Upstream repository {result.Parent.NameWithOwner} has {result.Parent.OpenPullRequests} open pull requests and {result.Parent.OpenIssues} open issues");
                    Console.WriteLine($"The default branch is {result.Parent.DefaultBranchName}");
                    Console.WriteLine($"Viewer has permission to {result.Parent.ViewerPermission}");
                }
                else
                {
                    Console.WriteLine($"Upstream repository {result.Repository.NameWithOwner} has {result.Repository.OpenPullRequests} open pull requests and {result.Repository.OpenIssues} open issues");
                    Console.WriteLine($"The default branch is {result.Repository.DefaultBranchName}");
                    Console.WriteLine($"Viewer has permission to {result.Repository.ViewerPermission}");
                }
            }
        }
    }

    [Command(Description = "Login using GitHub Credential Manager ")]
    class LoginCommand : GitHubCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var host = Host ?? "https://github.com";

            CredentialManager.Fill(host);

            await Task.Yield();
        }
    }


    [Command(Description = "Logout using GitHub Credential Manager")]
    class LogoutCommand : GitHubCommandBase
    {
        protected override async Task OnExecute(CommandLineApplication app)
        {
            var host = Host ?? "https://github.com";

            CredentialManager.Reject(host);

            await Task.Yield();
        }
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

        protected Connection CreateConnection(string host = null)
        {
            host = Host ?? host ?? "https://github.com";

            var productInformation = new ProductHeaderValue("Git-GitHub", "0.1");
            var token = GetToken(host);

            var hostAddress = HostAddress.Create(host);
            var connection = new Connection(productInformation, hostAddress.GraphQLUri, token);
            return connection;
        }

        protected string GetToken(string url)
        {
            var secretStore = CreateSecretStore();
            var auth = new BasicAuthentication(secretStore);
            var remoteUri = new Uri(url);
            var targetUrl = remoteUri.GetLeftPart(UriPartial.Authority);
            var creds = auth.GetCredentials(new TargetUri(targetUrl));
            if (creds is null)
            {
                throw new ApplicationException($"Couldn't find credentials for {targetUrl}");
            }

            return creds.Password;
        }

        SecretStore CreateSecretStore() =>  SecretStore switch
        {
            SecretStores.Git => new SecretStore("git", Secret.UriToIdentityUrl),
            SecretStores.GHfVS => new SecretStore("GitHub for Visual Studio", (tu, ns) => $"{ns} - {tu.ToString(true, true, true)}"),
            _ => throw new InvalidOperationException($"Unknown secret store {SecretStore}")
        };

        [Option("--host", Description = "The host URL")]
        public string Host { get; }

        [Option("--secret-store", Description = "The secret store to use (Git or GHfVS)")]
        public SecretStores SecretStore { get; } = SecretStores.Git;
    }

    public enum SecretStores
    {
        Git, GHfVS
    }
}
