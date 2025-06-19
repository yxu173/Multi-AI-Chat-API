using System.Text;
using System.Text.Json.Nodes;
using Application.Abstractions.Interfaces;
using Domain.Aggregates.Chats;
using Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Plugins;

public class CsvReaderPlugin : IChatPlugin<string>
{
    private readonly IFileAttachmentRepository _fileAttachmentRepository;
    private readonly ILogger<CsvReaderPlugin> _logger;

    public string Name => "csv_reader";
    public string Description => "Read and analyze CSV files that have been uploaded to the chat.";

    public CsvReaderPlugin(IFileAttachmentRepository fileAttachmentRepository, ILogger<CsvReaderPlugin> logger)
    {
        _fileAttachmentRepository = fileAttachmentRepository ?? throw new ArgumentNullException(nameof(fileAttachmentRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public JsonObject GetParametersSchema()
    {
        string schemaJson = """
                            {
                              "type": "object",
                              "properties": {
                                "file_id": {
                                  "type": "string",
                                  "description": "The ID of the CSV file to read."
                                },
                                "file_name": {
                                  "type": "string",
                                  "description": "The name of the CSV file to read (alternative to file_id)."
                                },
                                "max_rows": {
                                  "type": "integer",
                                  "description": "Maximum number of rows to read from the CSV (default: 100).",
                                  "default": 100
                                },
                                "analyze": {
                                  "type": "boolean",
                                  "description": "Whether to include basic analysis of the CSV data.",
                                  "default": true
                                },
                                "query": {
                                  "type": "string",
                                  "description": "Optional query to filter or find specific data in the CSV."
                                }
                              },
                              "oneOf": [
                                {"required": ["file_id"]},
                                {"required": ["file_name"]}
                              ]
                            }
                            """;
        return JsonNode.Parse(schemaJson)!.AsObject();
    }

    public async Task<PluginResult<string>> ExecuteAsync(JsonObject? arguments, CancellationToken cancellationToken = default)
    {
        if (arguments == null)
        {
            return new PluginResult<string>("Missing arguments for CSV reader.", false, "Either file_id or file_name must be provided.");
        }

        // Extract parameters
        string? fileId = null;
        string? fileName = null;
        int maxRows = 100;
        bool analyze = true;
        string? query = null;

        if (arguments.TryGetPropertyValue("file_id", out var fileIdNode) && fileIdNode is JsonValue fileIdValue &&
            fileIdValue.TryGetValue<string>(out var fileIdStr))
        {
            fileId = fileIdStr;
        }

        if (arguments.TryGetPropertyValue("file_name", out var fileNameNode) && fileNameNode is JsonValue fileNameValue &&
            fileNameValue.TryGetValue<string>(out var fileNameStr))
        {
            fileName = fileNameStr;
        }

        if (arguments.TryGetPropertyValue("max_rows", out var maxRowsNode) && maxRowsNode is JsonValue maxRowsValue &&
            maxRowsValue.TryGetValue<int>(out var maxRowsInt))
        {
            maxRows = Math.Min(1000, Math.Max(1, maxRowsInt)); // Clamp between 1 and 1000
        }

        if (arguments.TryGetPropertyValue("analyze", out var analyzeNode) && analyzeNode is JsonValue analyzeValue &&
            analyzeValue.TryGetValue<bool>(out var analyzeBool))
        {
            analyze = analyzeBool;
        }

        if (arguments.TryGetPropertyValue("query", out var queryNode) && queryNode is JsonValue queryValue &&
            queryValue.TryGetValue<string>(out var queryStr))
        {
            query = queryStr;
        }

        // Validate parameters
        if (string.IsNullOrEmpty(fileId) && string.IsNullOrEmpty(fileName))
        {
            return new PluginResult<string>("Please provide either a file_id or file_name parameter to identify which CSV file to read.", false, "Either file_id or file_name must be provided.");
        }

        try
        {
            // Get file attachment from repository
            FileAttachment? fileAttachment;
            if (!string.IsNullOrEmpty(fileId) && Guid.TryParse(fileId, out var id))
            {
                fileAttachment = await _fileAttachmentRepository.GetByIdAsync(id);
            }
            else if (!string.IsNullOrEmpty(fileName))
            {
                // Since GetAllAsync doesn't exist, we'll directly search the filesystem
                // Try different possible locations for the uploads directory
                string uploadsDirectory = "/home/yxu/self/Multi-AI-Chat-API/src/Web.Api/uploads";
                
                if (!Directory.Exists(uploadsDirectory))
                {
                    // Try alternative paths
                    var possiblePaths = new[]
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uploads"),
                        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../..", "Web.Api", "uploads")),
                        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "uploads")),
                        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "uploads")),
                        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "Web.Api", "uploads"))
                    };
                    
                    foreach (var path in possiblePaths)
                    {
                        if (Directory.Exists(path))
                        {
                            uploadsDirectory = path;
                            break;
                        }
                    }
                }
                
                _logger.LogInformation($"Searching for CSV file '{fileName}' in uploads directory: {uploadsDirectory}");
                
                var foundFilePath = FindCsvFileInUploads(uploadsDirectory, fileName);
                
                if (!string.IsNullOrEmpty(foundFilePath))
                {
                    var fileInfo = new FileInfo(foundFilePath);
                    fileAttachment = FileAttachment.Create(
                        fileName: fileName,
                        filePath: foundFilePath,
                        contentType: "text/csv",
                        fileSize: fileInfo.Length
                    );
                    
                    _logger.LogInformation($"Found CSV file at: {foundFilePath}");
                }
                else
                {
                    _logger.LogWarning($"Could not find CSV file '{fileName}' in uploads directory");
                    fileAttachment = null;
                }
            }
            else
            {
                return new PluginResult<string>("Invalid file ID or file name provided.", false, "Either file_id or file_name must be provided.");
            }

            if (fileAttachment == null)
            {
                return new PluginResult<string>("CSV file not found.", false, "CSV file not found.");
            }

            // Check if file exists and is a CSV
            if (fileAttachment.ContentType != "text/csv" && !fileAttachment.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return new PluginResult<string>("The specified file is not a CSV file.", false, "The specified file is not a CSV file.");
            }

            if (!File.Exists(fileAttachment.FilePath))
            {
                return new PluginResult<string>("CSV file not found on disk.", false, "CSV file not found on disk.");
            }

            var lines = await File.ReadAllLinesAsync(fileAttachment.FilePath, cancellationToken);
            if (lines.Length == 0)
            {
                return new PluginResult<string>("CSV file is empty.", true);
            }

            var header = ParseCsvLine(lines[0]);
            var data = new List<string[]>();
            
            for (int i = 1; i < Math.Min(lines.Length, maxRows + 1); i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    data.Add(ParseCsvLine(lines[i]));
                }
            }

            if (!string.IsNullOrEmpty(query))
            {
                data = FilterDataByQuery(data, header, query);
            }

            // Generate result
            var result = new StringBuilder();
            result.AppendLine($"# CSV File: {fileAttachment.FileName}");
            result.AppendLine();
            
            result.AppendLine("## File Information");
            result.AppendLine($"- **File Name**: {fileAttachment.FileName}");
            result.AppendLine($"- **File Size**: {FormatFileSize(fileAttachment.FileSize)}");
            result.AppendLine($"- **Total Rows**: {lines.Length - 1}");
            result.AppendLine($"- **Columns**: {header.Length}");
            result.AppendLine();

            result.AppendLine("## CSV Structure");
            result.AppendLine($"**Headers ({header.Length})**: {string.Join(", ", header)}");
            result.AppendLine();

            result.AppendLine("## Data Preview");
            if (data.Count == 0)
            {
                result.AppendLine("No data rows found in the CSV file.");
            }
            else
            {
                result.AppendLine(FormatMarkdownTable(header, data));
                
                if (lines.Length - 1 > data.Count)
                {
                    result.AppendLine($"\n*Showing {data.Count} of {lines.Length - 1} rows. Use max_rows parameter to adjust.*");
                }
            }

            if (analyze)
            {
                result.AppendLine("\n## Data Analysis");
                
                foreach (var columnAnalysis in AnalyzeCsvData(header, data))
                {
                    result.AppendLine(columnAnalysis);
                }
            }

            return new PluginResult<string>(result.ToString(), true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while reading CSV file");
            return new PluginResult<string>("", false, $"Error reading CSV file: {ex.Message}");
        }
    }

    private string[] ParseCsvLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return Array.Empty<string>();

        try
        {
            var result = new List<string>();
            bool inQuotes = false;
            var currentValue = new StringBuilder();
            bool hadQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    hadQuotes = true;
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentValue.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    if (hadQuotes)
                    {
                        var value = currentValue.ToString();
                        result.Add(value);
                    }
                    else
                    {
                        result.Add(currentValue.ToString().Trim());
                    }
                    currentValue.Clear();
                    hadQuotes = false;
                }
                else
                {
                    currentValue.Append(c);
                }
            }

            if (hadQuotes)
            {
                result.Add(currentValue.ToString());
            }
            else
            {
                result.Add(currentValue.ToString().Trim());
            }

            return result.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error parsing CSV line: {line}");
            // Fallback to simple splitting in case of error
            return line.Split(',');
        }
    }

    private List<string[]> FilterDataByQuery(List<string[]> data, string[] header, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return data;

        query = query.ToLowerInvariant();
        return data.Where(row => row.Any(cell => 
            cell != null && cell.ToLowerInvariant().Contains(query))).ToList();
    }

    private string FormatMarkdownTable(string[] header, List<string[]> data)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("| " + string.Join(" | ", header) + " |");
        
        sb.AppendLine("| " + string.Join(" | ", header.Select(_ => "---")) + " |");
        
        foreach (var row in data.Take(100))
        {
            var formattedRow = new string[header.Length];
            for (int i = 0; i < header.Length; i++)
            {
                formattedRow[i] = i < row.Length ? SanitizeForMarkdown(row[i]) : "";
            }
            
            sb.AppendLine("| " + string.Join(" | ", formattedRow) + " |");
        }
        
        return sb.ToString();
    }

    private string SanitizeForMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        
        return text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
    }

    private IEnumerable<string> AnalyzeCsvData(string[] header, List<string[]> data)
    {
        if (data.Count == 0)
            yield return "No data available for analysis.";

        for (int i = 0; i < header.Length; i++)
        {
            var columnName = header[i];
            var values = data.Select(row => i < row.Length ? row[i] : null)
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .ToList();
            
            var analysis = new StringBuilder();
            analysis.AppendLine($"### Column: {columnName}");
            analysis.AppendLine($"- **Total Values**: {values.Count}");
            analysis.AppendLine($"- **Empty/Missing Values**: {data.Count - values.Count}");
            
            bool isNumeric = values.All(v => decimal.TryParse(v, out _));
            if (isNumeric && values.Count > 0)
            {
                var numericValues = values.Select(v => decimal.Parse(v)).ToList();
                var min = numericValues.Min();
                var max = numericValues.Max();
                var avg = numericValues.Average();
                
                analysis.AppendLine($"- **Type**: Numeric");
                analysis.AppendLine($"- **Min**: {min}");
                analysis.AppendLine($"- **Max**: {max}");
                analysis.AppendLine($"- **Average**: {avg:0.##}");
            }
            else
            {
                var uniqueValues = values.Distinct().ToList();
                analysis.AppendLine($"- **Type**: Text");
                analysis.AppendLine($"- **Unique Values**: {uniqueValues.Count}");
                
                if (uniqueValues.Count <= 10 && uniqueValues.Count > 0)
                {
                    var valueCounts = values.GroupBy(v => v)
                                          .Select(g => new { Value = g.Key, Count = g.Count() })
                                          .OrderByDescending(x => x.Count)
                                          .Take(5);
                    
                    analysis.AppendLine("- **Most Common Values**:");
                    foreach (var item in valueCounts)
                    {
                        double percentage = (double)item.Count / values.Count * 100;
                        analysis.AppendLine($"  - {item.Value}: {item.Count} ({percentage:0.#}%)");
                    }
                }
            }
            
            yield return analysis.ToString();
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }
    
    private string? FindCsvFileInUploads(string uploadsDirectory, string fileName)
    {
        try
        {
            _logger.LogInformation($"Attempting to find CSV file: '{fileName}' in directory: '{uploadsDirectory}'");
            
            if (!Directory.Exists(uploadsDirectory))
            {
                _logger.LogWarning($"Uploads directory not found: {uploadsDirectory}");
                return null;
            }
            
            _logger.LogDebug($"Searching for direct match: {Path.Combine(uploadsDirectory, fileName)}");
            var filePath = Path.Combine(uploadsDirectory, fileName);
            if (File.Exists(filePath) && IsCsvFile(filePath))
            {
                _logger.LogInformation($"Direct match found: {filePath}");
                return filePath;
            }
            
            _logger.LogDebug("Searching in dated subdirectories...");
            foreach (var subdirectory in Directory.GetDirectories(uploadsDirectory))
            {
                _logger.LogDebug($"Checking subdirectory: {subdirectory}");
                filePath = Path.Combine(subdirectory, fileName);
                if (File.Exists(filePath) && IsCsvFile(filePath))
                {
                    _logger.LogInformation($"Found in subdirectory '{subdirectory}': {filePath}");
                    return filePath;
                }
                
                _logger.LogDebug($"Searching in sub-subdirectories of {subdirectory}...");
                foreach (var subsubdirectory in Directory.GetDirectories(subdirectory))
                {
                    _logger.LogDebug($"Checking sub-subdirectory: {subsubdirectory}");
                    filePath = Path.Combine(subsubdirectory, fileName);
                    if (File.Exists(filePath) && IsCsvFile(filePath))
                    {
                        _logger.LogInformation($"Found in sub-subdirectory '{subsubdirectory}': {filePath}");
                        return filePath;
                    }
                }
            }
            
            if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"File '{fileName}' does not end with .csv, attempting to find with .csv extension.");
                return FindCsvFileInUploads(uploadsDirectory, fileName + ".csv");
            }
            
            _logger.LogInformation($"Exact match for '{fileName}' not found by direct path or in dated subdirectories. Attempting fallback search for files with GUID prefix.");
            var allFiles = Directory.GetFiles(uploadsDirectory, "*.csv", SearchOption.AllDirectories);
            _logger.LogInformation($"Found {allFiles.Length} CSV files in total for fallback search (using SearchOption.AllDirectories from root uploads)."); // Changed from LogDebug

            // Added diagnostic logging for today's specific directory
            string todaysDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string todaysDirectory = Path.Combine(uploadsDirectory, todaysDate);
            _logger.LogInformation($"[DIAGNOSTIC] Checking specific dated directory for today: {todaysDirectory}");
            if (Directory.Exists(todaysDirectory))
            {
                var allFilesInTodaysDir = Directory.GetFiles(todaysDirectory, "*.*", SearchOption.TopDirectoryOnly);
                _logger.LogInformation($"[DIAGNOSTIC] Found {allFilesInTodaysDir.Length} total files in {todaysDirectory}: {string.Join(", ", allFilesInTodaysDir)}");
                var csvFilesInTodaysDir = Directory.GetFiles(todaysDirectory, "*.csv", SearchOption.TopDirectoryOnly);
                _logger.LogInformation($"[DIAGNOSTIC] Found {csvFilesInTodaysDir.Length} CSV files in {todaysDirectory}: {string.Join(", ", csvFilesInTodaysDir)}");
            }
            else
            {
                _logger.LogWarning($"[DIAGNOSTIC] Specific dated directory for today does not exist: {todaysDirectory}");
            }

            foreach (var f in allFiles)
            {
                string actualFileNameOnDisk = Path.GetFileName(f);
                int firstUnderscoreIndex = actualFileNameOnDisk.IndexOf('_');
                string comparableFileName;
                
                if (firstUnderscoreIndex > 0 && firstUnderscoreIndex < 40) { 
                    comparableFileName = actualFileNameOnDisk.Substring(firstUnderscoreIndex + 1);
                } else {
                    comparableFileName = actualFileNameOnDisk;
                }
                _logger.LogInformation($"Fallback: Checking file '{f}'. Original name on disk: '{actualFileNameOnDisk}'. Comparable name: '{comparableFileName}'. Target: '{fileName}'"); // This was correctly changed to LogInformation by user

                bool namesMatch = comparableFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase);
                bool namesWithoutExtensionMatch = Path.GetFileNameWithoutExtension(comparableFileName).Equals(Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase);

                if (namesMatch || namesWithoutExtensionMatch)
                {
                    _logger.LogInformation($"Fallback search found match: '{f}' (Comparable: '{comparableFileName}', Target: '{fileName}', NamesMatch: {namesMatch}, NamesWithoutExtMatch: {namesWithoutExtensionMatch})");
                    return f;
                }
            }
            
            _logger.LogWarning($"File '{fileName}' not found after all search strategies.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error searching for CSV file '{fileName}' in uploads directory: {uploadsDirectory}");
            return null;
        }
    }
    
    private bool IsCsvFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
               (File.Exists(filePath) && File.ReadAllLines(filePath).FirstOrDefault()?.Contains(',') == true);
    }
}
