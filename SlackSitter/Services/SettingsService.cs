using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SlackSitter.Services
{
    public class SettingsService
    {
        private const string EnvFileName = ".env";
        private readonly string _envFilePath;

        public class Settings
        {
            public string? AccessToken { get; set; }
        }

        public SettingsService()
        {
            _envFilePath = FindProjectRoot();
        }

        private string FindProjectRoot()
        {
            var appDirectory = AppContext.BaseDirectory;
            var currentDir = new DirectoryInfo(appDirectory);

            while (currentDir != null)
            {
                var csprojFiles = currentDir.GetFiles("*.csproj");
                if (csprojFiles.Length > 0)
                {
                    return Path.Combine(currentDir.Parent?.FullName ?? currentDir.FullName, EnvFileName);
                }

                var slnFiles = currentDir.GetFiles("*.sln");
                if (slnFiles.Length > 0)
                {
                    return Path.Combine(currentDir.FullName, EnvFileName);
                }

                currentDir = currentDir.Parent;
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), EnvFileName);
        }

        public Task<Settings> LoadSettingsAsync()
        {
            try
            {
                if (!File.Exists(_envFilePath))
                {
                    return Task.FromResult(new Settings());
                }

                var settings = new Settings();
                var lines = File.ReadAllLines(_envFilePath);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();

                        if (key == "SLACK_ACCESS_TOKEN")
                            settings.AccessToken = value;
                    }
                }

                return Task.FromResult(settings);
            }
            catch
            {
                return Task.FromResult(new Settings());
            }
        }

        public Task SaveSettingsAsync(Settings settings)
        {
            try
            {
                var directory = Path.GetDirectoryName(_envFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var lines = new List<string>
                {
                    "# Slack Authentication Token",
                    "# Do not commit this file to version control",
                    ""
                };

                if (!string.IsNullOrEmpty(settings.AccessToken))
                    lines.Add($"SLACK_ACCESS_TOKEN={settings.AccessToken}");

                File.WriteAllLines(_envFilePath, lines);
                System.Diagnostics.Debug.WriteLine($"Settings saved to: {_envFilePath}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        public string GetEnvFilePath()
        {
            return _envFilePath;
        }
    }
}
