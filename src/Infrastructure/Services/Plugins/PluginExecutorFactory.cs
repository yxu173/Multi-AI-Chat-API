using Application.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Services.AI.Builders;

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
            id: new Guid("0404f149-7b47-4052-babb-622a0c413fb3"),
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
            id: new Guid("83ba3d3c-1bb7-4473-a422-67f1c36fc4e1"),
            pluginType: typeof(JinaWebPlugin),
            name: "read_webpage",
            description: "Retrieve and summarize web content from a specific URL using Jina AI.",
            parametersSchemaJson: urlSchema
        );

        RegisterPlugin(
            id: new Guid("4fe051a4-2e77-4fa7-a05f-c1047627d8d1"),
            pluginType: typeof(HackerNewsSearchPlugin),
            name: "search_hacker_news",
            description: "Search Hacker News posts and comments using various filters like relevance, date, tags and more.",
            parametersSchemaJson: hackerNewsSearchSchema
        );
        
        RegisterPlugin(
            id: new Guid("3aa2ebec-99cb-4446-af77-762056b42410"),
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
            id: new Guid("19ffac4a-9ebf-4cc8-8bad-adfc8823af20"),
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
            id: new Guid("3d5ec31c-5e6c-437d-8494-2ca942c9e2fe"),
            pluginType: typeof(JinaDeepSearchPlugin),
            name: "jina_deepsearch",
            description: "Search the web with Jina's DeepSearch for real-time, comprehensive information.",
            parametersSchemaJson: jinaDeepSearchSchema
        );

        // Register DeepWiki MCP tool (no executor, just for OpenAI MCP)
        // var deepwikiPlugin = OpenAiPayloadBuilder.CreateDeepWikiMcpTool();
        // _pluginRegistry[deepwikiPlugin.Id] = new PluginRegistration
        // {
        //     Id = deepwikiPlugin.Id,
        //     Type = typeof(object), // No executor needed for MCP
        //     Name = deepwikiPlugin.Name,
        //     Description = deepwikiPlugin.Description,
        //     ParametersSchema = deepwikiPlugin.ParametersSchema
        // };
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

    public IChatPlugin<string> GetPlugin(Guid pluginId)
    {
        if (!_pluginRegistry.TryGetValue(pluginId, out var pluginInfo))
        {
            throw new ArgumentException($"No plugin registered with ID: {pluginId}");
        }
        return (IChatPlugin<string>)_serviceProvider.GetRequiredService(pluginInfo.Type);
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