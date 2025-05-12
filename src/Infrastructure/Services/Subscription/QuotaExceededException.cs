namespace Infrastructure.Services.Subscription;

public class QuotaExceededException : Exception
{
    public QuotaExceededException(string message) : base(message)
    {
    }

    public QuotaExceededException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
