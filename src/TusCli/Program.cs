﻿using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using TusDotNetClient;
using static System.Console;

namespace TusCli
{
    [Command("tus", Description = "A cli tool for interacting with a Tus enabled server.")]
    class Program
    {
        private static Task Main(string[] args) => CommandLineApplication.ExecuteAsync<Program>(args);

        // ReSharper disable UnassignedGetOnlyAutoProperty
        [Argument(0, "file", "File to upload")]
        [Required]
        public string FilePath { get; }

        [Argument(1, "address", "The endpoint of the Tus server")]
        [Required]
        public string Address { get; }

        [Option(Description = "Additional metadata to submit. Format: key1=value1,key2=value2")]
        public string Metadata { get; }

        [Option(Description = "The size (in MB) of each chunk when uploading (default: 5)")]
        public double ChunkSize { get; } = 5;
        // ReSharper restore UnassignedGetOnlyAutoProperty

        public async Task<int> OnExecuteAsync()
        {
            var file = new FileInfo(FilePath);
            if (!file.Exists)
            {
                Error.WriteLine($"Could not find file '{file.FullName}'.");
                return 1;
            }

            var infoFile = new FileInfo($"{file.FullName}.info");
            var fileInformation = FileInformation.Parse(TryReadAllText(infoFile.FullName) ?? "");

            var metadata = ParseMetadata(Metadata) ?? Array.Empty<(string, string)>();

            var client = new TusClient();

            try
            {
                var fileUrl = $"{Address}{fileInformation.ServerId}";
                if (string.IsNullOrWhiteSpace(fileInformation.ServerId))
                {
                    fileUrl = await client.CreateAsync(Address, file.Length, metadata);
                    fileInformation.ServerId = fileUrl.Split('/').Last();
                }

                File.WriteAllText(infoFile.FullName, fileInformation.ToString());
                
                var operation = client.UploadAsync(fileUrl, file, ChunkSize);
                operation.Progressed += OnUploadProgress;
                await operation;
                
                try
                {
                    infoFile.Delete();
                }
                catch
                {
                    // ignored
                }
            }
            catch (Exception e)
            {
                WriteLine();
                Error.WriteLine($"Operation failed with message: '{e.Message}'");
                return 2;
            }

            return 0;
        }

        private void OnUploadProgress(long bytesTransferred, long bytesTotal)
        {
            var progress = bytesTransferred / (double) bytesTotal;
            var percentString = $"{progress * 100:0.00}%".PadRight(8);
            var progressBarMaxWidth = BufferWidth - percentString.Length - 2;
            var progressBar = Enumerable.Range(0, (int) Math.Round(progressBarMaxWidth * progress))
                .Select(_ => '=')
                .ToArray();
            if (progress < 1 && progressBar.Length > 0)
                progressBar[progressBar.Length - 1] = '>';
            SetCursorPosition(0, CursorTop);
            Write($"{percentString}[{string.Join("", progressBar).PadRight(progressBarMaxWidth)}]");
        }

        private static string TryReadAllText(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch
            {
                return null;
            }
        }

        private static (string, string)[] ParseMetadata(string metadata) =>
            metadata?
                .Split(',')
                .Select(md =>
                {
                    var parts = md.Split('=');
                    if (parts.Length == 2)
                        return (parts[0], parts[1]);

                    var response =
                        Prompt.GetString($"Unable to parse '{md}'. Do you want to [s]kip it or [a]bort?")
                        ?? "a";
                    if (!"skip".StartsWith(response, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("Aborted by user request.");
                    
                    return (null, null);
                })
                .Where(data => data.Item1 != null && data.Item2 != null)
                .ToArray();
    }
}