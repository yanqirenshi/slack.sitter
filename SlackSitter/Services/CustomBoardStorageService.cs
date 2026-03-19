using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SlackSitter.Services
{
    public class CustomBoardStorageService
    {
        private const string FileName = "custom-board.json";
        private readonly string _filePath;

        public class CustomBoardState
        {
            public bool IsVisible { get; set; }
            public List<string> SelectedChannels { get; set; } = new();
        }

        public CustomBoardStorageService()
        {
            var appDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SlackSitter");
            _filePath = Path.Combine(appDataDirectory, FileName);
        }

        public async Task<CustomBoardState> LoadAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new CustomBoardState();
                }

                await using var stream = File.OpenRead(_filePath);
                var state = await JsonSerializer.DeserializeAsync<CustomBoardState>(stream);
                return state ?? new CustomBoardState();
            }
            catch
            {
                return new CustomBoardState();
            }
        }

        public async Task SaveAsync(IEnumerable<string> selectedChannels, bool isVisible)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var state = new CustomBoardState
                {
                    IsVisible = isVisible,
                    SelectedChannels = selectedChannels
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };

                await using var stream = File.Create(_filePath);
                await JsonSerializer.SerializeAsync(stream, state, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch
            {
            }
        }

        public string GetFilePath()
        {
            return _filePath;
        }
    }
}
