using Application.Abstractions.Interfaces;
using Application.Services.Helpers;
using Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Services.PayloadBuilders;

public class PayloadBuilderFactory : IPayloadBuilderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public PayloadBuilderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }
    
    public IOpenAiPayloadBuilder CreateOpenAiBuilder()
    {
        return _serviceProvider.GetRequiredService<IOpenAiPayloadBuilder>();
    }

    public IAnthropicPayloadBuilder CreateAnthropicBuilder()
    {
        return _serviceProvider.GetRequiredService<IAnthropicPayloadBuilder>();
    }

    public IGeminiPayloadBuilder CreateGeminiBuilder()
    {
        return _serviceProvider.GetRequiredService<IGeminiPayloadBuilder>();
    }

    public IDeepSeekPayloadBuilder CreateDeepSeekBuilder()
    {
        return _serviceProvider.GetRequiredService<IDeepSeekPayloadBuilder>();
    }

    public IAimlFluxPayloadBuilder CreateAimlFluxBuilder()
    {
        return _serviceProvider.GetRequiredService<IAimlFluxPayloadBuilder>();
    }

    public IPayloadBuilder CreateImagenBuilder()
    {
        return _serviceProvider.GetRequiredService<ImagenPayloadBuilder>();
    }

    public IGrokPayloadBuilder CreateGrokBuilder()
    {
        return _serviceProvider.GetRequiredService<IGrokPayloadBuilder>();
    }
} 