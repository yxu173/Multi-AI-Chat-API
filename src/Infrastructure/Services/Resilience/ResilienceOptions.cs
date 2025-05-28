using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services.Resilience
{
    public class ResilienceOptions
    {
        public CachingOptions Caching { get; set; } = new();


        public RetryPolicyOptions RetryPolicy { get; set; } = new();


        public TimeoutPolicyOptions TimeoutPolicy { get; set; } = new();


        public static ResilienceOptions FromConfiguration(IConfiguration configuration)
        {
            var options = new ResilienceOptions();
            var section = configuration.GetSection("Resilience");

            if (section.Exists())
            {
                section.Bind(options);
            }

            return options;
        }
    }


    public class CachingOptions
    {
        public int DefaultExpirationInMinutes { get; set; } = 60;
    }


    public class RetryPolicyOptions
    {
        public int MaxRetryAttempts { get; set; } = 3;


        public int InitialDelayInSeconds { get; set; } = 1;


        public int MaxDelayInSeconds { get; set; } = 5;
    }


    public class TimeoutPolicyOptions
    {
        public int TimeoutInSeconds { get; set; } = 150;
    }
}