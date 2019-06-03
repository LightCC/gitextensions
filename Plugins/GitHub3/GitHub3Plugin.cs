using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Git.hub;
using GitCommands.Config;
using GitHub3.Properties;
using GitUIPluginInterfaces;
using GitUIPluginInterfaces.RepositoryHosts;
using ResourceManager;

namespace GitHub3
{
    internal static class GitHubApiInfo
    {
        internal static string client_id = "ebc0e8947c206610d737";
        internal static string client_secret = "c993907df3f45145bf638842692b69c56d1ace4d";
    }

    internal static class GitHubLoginInfo
    {
        private static string _username;
        public static string Username
        {
            get
            {
                if (_username == "")
                {
                    return null;
                }

                if (_username != null)
                {
                    return _username;
                }

                try
                {
                    var user = GitHub3Plugin.GitHub.getCurrentUser();
                    if (user != null)
                    {
                        _username = user.Login;
                        ////MessageBox.Show("GitHub username: " + _username);
                        return _username;
                    }
                    else
                    {
                        _username = "";
                    }

                    return null;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static string OAuthToken
        {
            get => GitHub3Plugin.Instance.OAuthToken.ValueOrDefault(GitHub3Plugin.Instance.Settings);
            set
            {
                _username = null;
                GitHub3Plugin.Instance.OAuthToken[GitHub3Plugin.Instance.Settings] = value;
                GitHub3Plugin.GitHub.setOAuth2Token(value);
            }
        }
    }

    internal static class GitHubLoginInfo_Enterprise
    {
        private static string _username;
        public static string Username
        {
            get
            {
                if (_username == "")
                {
                    return null;
                }

                if (_username != null)
                {
                    return _username;
                }

                try
                {
                    var user = GitHub3Plugin.GitHubEnterprise.getCurrentUser();
                    if (user != null)
                    {
                        _username = user.Login;
                        ////MessageBox.Show("GitHub username: " + _username);
                        return _username;
                    }
                    else
                    {
                        _username = "";
                    }

                    return null;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static string OAuthToken
        {
            get => GitHub3Plugin.Instance.OAuthToken_gitHubEnterprise.ValueOrDefault(GitHub3Plugin.Instance.Settings);
            set
            {
                _username = null;
                GitHub3Plugin.Instance.OAuthToken_gitHubEnterprise[GitHub3Plugin.Instance.Settings] = value;
                GitHub3Plugin.GitHubEnterprise.setOAuth2Token(value);
            }
        }

        public static bool IsUsing
        {
            get => GitHub3Plugin.Instance.IsUsingGitHubEnterpriseSettings.ValueOrDefault(GitHub3Plugin.Instance.Settings);
            set => GitHub3Plugin.Instance.IsUsingGitHubEnterpriseSettings[GitHub3Plugin.Instance.Settings] = value;
        }

        public static string ApiUrl
        {
            get => GitHub3Plugin.Instance.GitHubEnterpriseDomain.ValueOrDefault(GitHub3Plugin.Instance.Settings);
        }
    }

    [Export(typeof(IGitPlugin))]
    public class GitHub3Plugin : GitPluginBase, IRepositoryHostPlugin
    {
        // TODO add caption to StringSettings
        public readonly PasswordSetting OAuthToken = new PasswordSetting("OAuth Token for github.com", "");
        public readonly BoolSetting IsUsingGitHubEnterpriseSettings = new BoolSetting("Use GitHub Enterprise", false);
        public readonly StringSetting GitHubEnterpriseDomain = new StringSetting("Github Enterprise Domain", "");
        public readonly PasswordSetting OAuthToken_gitHubEnterprise = new PasswordSetting("OAuth Token (github.com)", "");

        internal static GitHub3Plugin Instance;

        // TODO: can this stay static if we are switching between different github instances?
        internal static Client GitHub;
        internal static Client GitHubEnterprise;

        public GitHub3Plugin()
        {
            SetNameAndDescription("GitHub");
            Translate();

            if (Instance == null)
            {
                Instance = this;
            }

            GitHub = new Client();
            GitHubEnterprise = null;

            Icon = Resources.IconGitHub;
        }

        public override IEnumerable<ISetting> GetSettings()
        {
            yield return OAuthToken;
            yield return IsUsingGitHubEnterpriseSettings;
            yield return GitHubEnterpriseDomain;
            yield return OAuthToken_gitHubEnterprise;
        }

        public override void Register(IGitUICommands gitUiCommands)
        {
            if (!string.IsNullOrEmpty(GitHubLoginInfo.OAuthToken))
            {
                GitHub.setOAuth2Token(GitHubLoginInfo.OAuthToken);
            }
        }

        public override bool Execute(GitUIEventArgs args)
        {
            // TODO: Update to check if using GitHubEnterprise and create/use that instance if so.
            // This is using the new GitHub3Plugin.GitHubLoginInfo_Enterprise static class.
            if (!GitHubLoginInfo_Enterprise.IsUsing)
            {
                if (string.IsNullOrEmpty(GitHubLoginInfo.OAuthToken))
                {
                    using (var frm = new OAuth())
                    {
                        frm.ShowDialog(args.OwnerForm);
                    }
                }
                else
                {
                    MessageBox.Show(args.OwnerForm, "You already have an OAuth token. To get a new one, delete your old one in Plugins > Settings first.");
                }
            }
            else
            {
                // We are using Github Enterprise, so write code for that here!!
                MessageBox.Show(args.OwnerForm, "Using GitHub Enterprise!!");
            }

            return false;
        }

        // --

        public IReadOnlyList<IHostedRepository> SearchForRepository(string search)
        {
            return GitHub.searchRepositories(search).Select(repo => (IHostedRepository)new GitHubRepo(repo)).ToList();
        }

        public IReadOnlyList<IHostedRepository> GetRepositoriesOfUser(string user)
        {
            return GitHub.getRepositories(user).Select(repo => (IHostedRepository)new GitHubRepo(repo)).ToList();
        }

        public IHostedRepository GetRepository(string user, string repositoryName)
        {
            return new GitHubRepo(GitHub.getRepository(user, repositoryName));
        }

        public IReadOnlyList<IHostedRepository> GetMyRepos()
        {
            return GitHub.getRepositories().Select(repo => (IHostedRepository)new GitHubRepo(repo)).ToList();
        }

        public bool ConfigurationOk => true;

        public bool GitModuleIsRelevantToMe(IGitModule module)
        {
            return GetHostedRemotesForModule(module).Count > 0;
        }

        /// <summary>
        /// Returns all relevant github-remotes for the current working directory
        /// </summary>
        public IReadOnlyList<IHostedRemote> GetHostedRemotesForModule(IGitModule module)
        {
            return Remotes().ToList();

            IEnumerable<IHostedRemote> Remotes()
            {
                var set = new HashSet<IHostedRemote>();

                foreach (string remote in module.GetRemoteNames())
                {
                    var url = module.GetSetting(string.Format(SettingKeyString.RemoteUrl, remote));

                    if (string.IsNullOrEmpty(url))
                    {
                        continue;
                    }

                    // TODO: update these regex's to use the new variable github.com URL
                    var m = Regex.Match(url, @"git(?:@|://)github.com[:/]([^/]+)/([\w_\.\-]+)\.git");
                    if (!m.Success)
                    {
                        m = Regex.Match(url, @"https?://(?:[^@:]+)?(?::[^/@:]+)?@?github.com/([^/]+)/([\w_\.\-]+)(?:.git)?");
                    }

                    if (m.Success)
                    {
                        var hostedRemote = new GitHubHostedRemote(remote, m.Groups[1].Value, m.Groups[2].Value.Replace(".git", ""));

                        if (set.Add(hostedRemote))
                        {
                            yield return hostedRemote;
                        }
                    }
                }
            }
        }
    }
}
