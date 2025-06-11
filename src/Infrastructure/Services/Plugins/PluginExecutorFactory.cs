using Application.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Infrastructure.Services.Plugins;

public class PluginExecutorFactory : IPluginExecutorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDictionary<Guid, PluginRegistration> _pluginRegistry;

    public PluginExecutorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _pluginRegistry = new Dictionary<Guid, PluginRegistration>();

        string searchQuerySchema = """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "The search query to execute."
            }
          },
          "required": ["query"]
        }
        """;

        string urlSchema = """
        {
          "type": "object",
          "properties": {
            "url": {
              "type": "string",
              "description": "The URL of the webpage to read."
            }
          },
          "required": ["url"]
        }
        """;

        string perplexityQuerySchema = """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "The query or prompt for Perplexity AI."
            }
          },
          "required": ["query"]
        }
        """;


      string hackerNewsSearchSchema = """
      {
        "type": "object",
        "properties": {
          "query": {
            "type": "string",
            "description": "The search query to execute."
          }
        },
        "required": ["query"]
      }
      """;

        string wikipediaSearchSchema = """
      {
        "type": "object",
        "properties": {
          "query": {
            "type": "string",
            "description": "The search term to look up on Wikipedia."
          },
          "limit": {
            "type": "integer",
            "description": "Maximum number of results to return (1-10).",
            "default": 3
          }
        },
        "required": ["query"]
      }
      """;

        RegisterPlugin(
            id: new Guid("758ca6d5-75a2-487b-b929-52489d3b1d2d"),
            pluginType: typeof(WebSearchPlugin),
            name: "google_search",
            description: "Search the web using Google for real-time information.",
            parametersSchemaJson: searchQuerySchema
        );

        RegisterPlugin(
            id: new Guid("6A2ADF8A-9B2F-4F9D-8F31-B29A9F1C1760"),
            pluginType: typeof(PerplexityPlugin),
            name: "perplexity_search",
            description: "Advanced research assistant using the Perplexity Sonar API.",
            parametersSchemaJson: perplexityQuerySchema
        );

        RegisterPlugin(
            id: new Guid("678eaf7e-7e0d-4513-83a3-738f55dab692"),
            pluginType: typeof(JinaWebPlugin),
            name: "read_webpage",
            description: "Retrieve and summarize web content from a specific URL using Jina AI.",
            parametersSchemaJson: urlSchema
        );

        RegisterPlugin(
            id: new Guid("a6801802-2361-4ffd-ba43-4083d1ea5de2"),
            pluginType: typeof(HackerNewsSearchPlugin),
            name: "search_hacker_news",
            description: "Search Hacker News posts and comments using various filters like relevance, date, tags and more.",
            parametersSchemaJson: hackerNewsSearchSchema
        );
        
        RegisterPlugin(
            id: new Guid("8cadae36-bd7b-40d0-85d9-4070a4cea51f"),
            pluginType: typeof(WikipediaPlugin),
            name: "wikipedia_search",
            description: "Search Wikipedia for information on a specific topic or term",
            parametersSchemaJson: wikipediaSearchSchema
        );

        string csvReaderSchema = """
      {
        "type": "object",
        "properties": {
          "file_id": {
            "type": "string",
            "description": "The ID of the CSV file to read (provide either file_id or file_name)."
          },
          "file_name": {
            "type": "string",
            "description": "The name of the CSV file to read (provide either file_id or file_name)."
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
        }
      }
      """;

        RegisterPlugin(
            id: new Guid("00fb0c45-ba1b-469d-8c2d-072fb8025b44"),
            pluginType: typeof(CsvReaderPlugin),
            name: "csv_reader",
            description: "Read and analyze CSV files that have been uploaded to the chat.",
            parametersSchemaJson: csvReaderSchema
        );
        
        string jinaDeepSearchSchema = """
      {
        "type": "object",
        "properties": {
          "query": {
            "type": "string",
            "description": "The search query to execute with Jina DeepSearch."
          }
        },
        "required": ["query"]
      }
      """;

        RegisterPlugin(
            id: new Guid("235979c5-cec1-4af2-9d61-6c1079c80be5"),
            pluginType: typeof(JinaDeepSearchPlugin),
            name: "jina_deepsearch",
            description: "Search the web with Jina's DeepSearch for real-time, comprehensive information.",
            parametersSchemaJson: jinaDeepSearchSchema
        );
    }

    private void RegisterPlugin(Guid id, Type pluginType, string name, string description, string parametersSchemaJson)
    {
        JsonObject? schemaObject = null;
        try
        {
            schemaObject = JsonSerializer.Deserialize<JsonObject>(parametersSchemaJson);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Error parsing schema for plugin {name} ({id}): {ex.Message}");
        }

        _pluginRegistry[id] = new PluginRegistration
        {
            Id = id,
            Type = pluginType,
            Name = name,
            Description = description,
            ParametersSchema = schemaObject
        };
    }

    public IChatPlugin GetPlugin(Guid pluginId)
    {
        if (!_pluginRegistry.TryGetValue(pluginId, out var pluginInfo))
        {
            throw new ArgumentException($"No plugin registered with ID: {pluginId}");
        }
        return (IChatPlugin)_serviceProvider.GetRequiredService(pluginInfo.Type);
    }

    public IEnumerable<PluginDefinition> GetAllPluginDefinitions()
    {
        return _pluginRegistry.Values.Select(reg => new PluginDefinition(
            reg.Id,
            reg.Name,
            reg.Description,
            reg.ParametersSchema
        )).ToList();
    }

    private class PluginRegistration
    {
        public Guid Id { get; set; }
        public Type Type { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public JsonObject? ParametersSchema { get; set; }
    }
}