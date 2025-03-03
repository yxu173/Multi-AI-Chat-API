using Application.Services;

namespace Infrastructure.Services;

public class TokenCountingService
{
    
    private const int CHARS_PER_TOKEN = 4;
    
    public int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
            
        return (int)Math.Ceiling((double)text.Length / CHARS_PER_TOKEN);
    }
    
    public int EstimateInputTokens(IEnumerable<MessageDto> messages)
    {
        return messages
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .Sum(m => EstimateTokenCount(m.Content));
    }
    
    public decimal CalculateCost(int inputTokens, int outputTokens, double inputPricePer1K, double outputPricePer1K)
    {
        decimal inputCost = (decimal)(inputTokens * inputPricePer1K / 1000);
        decimal outputCost = (decimal)(outputTokens * outputPricePer1K / 1000);
        return Math.Round(inputCost + outputCost, 6); 
    }
}