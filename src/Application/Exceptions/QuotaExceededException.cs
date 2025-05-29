using System; // Added for Exception

namespace Application.Exceptions; // Changed namespace

public class QuotaExceededException : Exception
{
    public QuotaExceededException(string message) : base(message)
    {
    }

    public QuotaExceededException(string message, Exception innerException) : base(message, innerException)
    {
    }
} 