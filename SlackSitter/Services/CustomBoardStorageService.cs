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
            public string ActiveFilter { get; set; } = "JoinedOnly";
            public int ActiveCustomBoardIndex { get; set; } = -1;
            public List<CustomBoardDefinition> CustomBoards { get; set; } = new();
            public bool IsVisible { get; set; }
            public List<string> SelectedChannels { get; set; } = new();
        }

        public class CustomBoardDefinition
        {
            public string Name { get; set; } = string.Empty;
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
                if (state == null)
                {
                    return new CustomBoardState();
                }

                if (state.CustomBoards.Count == 0 && state.SelectedChannels.Count > 0)
                {
                    state.CustomBoards.Add(new CustomBoardDefinition
                    {
                        Name = string.Empty,
                        SelectedChannels = state.SelectedChannels
                    });
                    state.ActiveCustomBoardIndex = state.IsVisible ? 0 : -1;
                }

                return state;
            }
            catch
            {
                return new CustomBoardState();
            }
        }

        public async Task SaveAsync(IEnumerable<CustomBoardDefinition> customBoards, int activeCustomBoardIndex, string activeFilter)
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
                    ActiveFilter = string.IsNullOrWhiteSpace(activeFilter) ? "JoinedOnly" : activeFilter,
                    ActiveCustomBoardIndex = activeCustomBoardIndex,
                    CustomBoards = customBoards
                        .Select(board => new CustomBoardDefinition
                        {
                            Name = board.Name?.Trim() ?? string.Empty,
                            SelectedChannels = board.SelectedChannels
                                .Where(name => !string.IsNullOrWhiteSpace(name))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList()
                        })
                        .Where(board => board.SelectedChannels.Count > 0)
                        .ToList()
                };

                state.IsVisible = state.CustomBoards.Count > 0;
                state.SelectedChannels = state.CustomBoards.FirstOrDefault()?.SelectedChannels ?? new List<string>();

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
